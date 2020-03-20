using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SegmentChallengeWeb.Configuration;
using SegmentChallengeWeb.Controllers;
using SegmentChallengeWeb.Models;
using SegmentChallengeWeb.Persistence;

namespace SegmentChallengeWeb {
    public class EffortRefreshService {
        private readonly IOptions<StravaConfiguration> stravaConfiguration;
        private readonly Func<DbConnection> dbConnectionFactory;
        private readonly StravaApiHelper apiHelper;
        private readonly ILogger<EffortRefreshService> logger;

        public EffortRefreshService(
            IOptions<StravaConfiguration> stravaConfiguration,
            Func<DbConnection> dbConnectionFactory,
            StravaApiHelper apiHelper,
            ILogger<EffortRefreshService> logger) {
            this.stravaConfiguration = stravaConfiguration;
            this.dbConnectionFactory = dbConnectionFactory;
            this.apiHelper = apiHelper;
            this.logger = logger;
        }

        public async Task RefreshAthleteEfforts(
            Int32 updateId,
            String challengeName,
            Int64 athleteId,
            CancellationToken cancellationToken) {
            this.logger.LogDebug(
                "Refreshing all Efforts for Challenge {ChallengeName} (Update {UpdateId})",
                challengeName,
                updateId
            );

            try {
                await using var connection = this.dbConnectionFactory();
                await connection.OpenAsync(cancellationToken);

                await using var dbContext = new SegmentChallengeDbContext(connection);

                var challengeTable = dbContext.Set<Challenge>();
                var registrationsTable = dbContext.Set<ChallengeRegistration>();
                var athletesTable = dbContext.Set<Athlete>();
                var updatesTable = dbContext.Set<Update>();

                var challenge = await challengeTable.SingleOrDefaultAsync(
                    c => c.Name == challengeName,
                    cancellationToken
                );

                if (challenge == null) {
                    this.logger.LogError(
                        "Refresh Efforts Failed. Challenge not found: {ChallengeName}",
                        challengeName
                    );

                    return;
                }

                var athlete = await
                    registrationsTable
                        .Join(
                            athletesTable,
                            cr => cr.AthleteId,
                            a => a.Id,
                            (cr, a) => new { Registration = cr, Athlete = a })
                        .Where(ra => ra.Registration.ChallengeId == challenge.Id && ra.Athlete.Id == athleteId)
                        .Where(ra => ra.Athlete.Gender != null && ra.Athlete.BirthDate != null)
                        .Select(ra => ra.Athlete)
                        .FirstOrDefaultAsync(cancellationToken);

                if (athlete == null) {
                    this.logger.LogError(
                        "Refresh Efforts Failed. Athlete not registered: {AthleteId} {ChallengeName}",
                        athleteId,
                        challengeName
                    );

                    return;
                }

                var update = await updatesTable.FindAsync(new Object[] { updateId }, cancellationToken);
                update.StartTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                var (activitiesUpdated, activitiesSkipped, effortsUpdated, error) =
                    await RefreshAthleteEffortsInternal(
                        dbContext,
                        update,
                        challenge,
                        athlete,
                        cancellationToken);

                update.ActivityCount += activitiesUpdated;
                update.SkippedActivityCount += activitiesSkipped;
                update.EffortCount += effortsUpdated;
                if (error) {
                    update.ErrorCount++;
                }

                update.AthleteCount = 1;
                update.Progress = 1;
                update.EndTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            } catch (Exception ex) {
                this.logger.LogError(
                    "Refresh Efforts Failed. Unexpected Exception: {Message}",
                    1,
                    ex,
                    ex.Message
                );
            }
        }

        public async Task RefreshEfforts(
            Int32 updateId,
            String challengeName,
            CancellationToken cancellationToken) {

            this.logger.LogDebug(
                "Refreshing all Efforts for Challenge {ChallengeName} (Update {UpdateId})",
                challengeName,
                updateId
            );

            try {
                await using var connection = this.dbConnectionFactory();
                await connection.OpenAsync(cancellationToken);

                await using var dbContext = new SegmentChallengeDbContext(connection);

                var challengeTable = dbContext.Set<Challenge>();
                var registrationsTable = dbContext.Set<ChallengeRegistration>();
                var athletesTable = dbContext.Set<Athlete>();
                var updatesTable = dbContext.Set<Update>();

                var challenge = await challengeTable.SingleOrDefaultAsync(
                    c => c.Name == challengeName,
                    cancellationToken
                );

                if (challenge == null) {
                    this.logger.LogError(
                        "Refresh Efforts Failed. Challenge not found: {ChallengeName}",
                        challengeName
                    );

                    return;
                }

                var athletes = await
                    registrationsTable
                        .Join(
                            athletesTable,
                            cr => cr.AthleteId,
                            a => a.Id,
                            (cr, a) => new { Registration = cr, Athlete = a })
                        .Where(ra => ra.Registration.ChallengeId == challenge.Id)
                        .Where(ra => ra.Athlete.Gender != null && ra.Athlete.BirthDate != null)
                        .Select(ra => ra.Athlete)
                        .ToListAsync(cancellationToken);

                var update = await updatesTable.FindAsync(new Object[] { updateId }, cancellationToken);
                update.StartTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                foreach (var athlete in athletes) {
                    this.logger.LogDebug(
                        "Updating {ChallengeName} efforts for athlete {AthleteId}.",
                        challengeName,
                        athlete.Id
                    );

                    // Update efforts
                    try {
                        var (activitiesUpdated, activitiesSkipped, effortsUpdated, error) =
                            await RefreshAthleteEffortsInternal(
                                dbContext,
                                update,
                                challenge,
                                athlete,
                                cancellationToken);

                        update.ActivityCount += activitiesUpdated;
                        update.SkippedActivityCount += activitiesSkipped;
                        update.EffortCount += effortsUpdated;
                        if (error) {
                            update.ErrorCount++;
                        }
                    } catch (TaskCanceledException) {
                        throw;
                    } catch (OperationCanceledException) {
                        throw;
                    } catch (Exception ex) {
                        update.ErrorCount++;
                        this.logger.LogError(
                            "An unexpected exception occurred while refreshing efforts for Athlete {AthleteId} (Challenge: {ChallengeId}",
                            2,
                            ex,
                            athlete.Id,
                            challenge.Id
                        );
                    }

                    update.AthleteCount++;

                    update.Progress =
                        (Single)update.AthleteCount / athletes.Count;

                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                update.EndTime = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            } catch (Exception ex) {
                this.logger.LogError(
                    "Refresh Efforts Failed. Unexpected Exception: {Message}",
                    1,
                    ex,
                    ex.Message
                );
            }
        }

        private async Task<(Int32 activitiesUpdated, Int32 activitiesSkipped, Int32 effortsUpdated,
                Boolean error)>
            RefreshAthleteEffortsInternal(
                SegmentChallengeDbContext dbContext,
                Update update,
                Challenge challenge,
                Athlete athlete,
                CancellationToken cancellationToken) {
            // var athletesTable = dbContext.Set<Athlete>();
            var effortsTable = dbContext.Set<Effort>();
            var activityUpdatesTable = dbContext.Set<ActivityUpdate>();

            var stravaClient =
                new HttpClient {
                    BaseAddress = new Uri("https://www.strava.com")
                };

            // If the athlete's token is expired, refresh it
            // In theory we should do this before each call, but we'll fudge it by assuming we can
            // handle a single athlete in fewer than 10 minutes.
            if (athlete.TokenExpiration <= DateTime.UtcNow.AddMinutes(-10)) {
                var response =
                    await this.apiHelper.MakeThrottledApiRequest(
                        () => stravaClient.PostAsync(
                            "/api/v3/oauth/token",
                            new FormUrlEncodedContent(new Dictionary<string, string> {
                                { "client_id", this.stravaConfiguration.Value.ClientId },
                                { "client_secret", this.stravaConfiguration.Value.ClientSecret },
                                { "refresh_token", athlete.RefreshToken },
                                { "grant_type", "refresh_token" }
                            }), cancellationToken),
                        cancellationToken
                    );

                if (response.IsSuccessStatusCode) {
                    var session =
                        await response.Content.ReadAsAsync<StravaSession>(cancellationToken);

                    athlete.AccessToken = session.AccessToken;
                    athlete.RefreshToken = session.RefreshToken;
                    athlete.TokenExpiration =
                        StravaApiHelper.DateTimeFromUnixTime(session.ExpiresAt);

                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            stravaClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", athlete.AccessToken);

            var previousUpdates = await
                activityUpdatesTable
                    .Where(u => u.AthleteId == athlete.Id && u.ChallengeId == challenge.Id)
                    .ToDictionaryAsync(u => u.ActivityId, cancellationToken);

            var activitiesUpdated = 0;
            var activitiesSkipped = 0;
            var effortsUpdated = 0;

            // For each activity between start and end of challenge
            for (var pageNumber = 1; !cancellationToken.IsCancellationRequested; pageNumber++) {
                var response =
                    await this.apiHelper.MakeThrottledApiRequest(
                        () => stravaClient.GetAsync(
                            $"/api/v3/athlete/activities?after={challenge.StartDate.ToUnixTime()}&before={challenge.EndDate.ToUnixTime()}&page={pageNumber}&per_page=200",
                            cancellationToken),
                        cancellationToken
                    );

                if (response.IsSuccessStatusCode) {
                    var activities =
                        await response.Content.ReadAsAsync<StravaActivity[]>(cancellationToken);
                    response.Dispose();
                    response = null;

                    if (activities == null || activities.Length == 0) {
                        break;
                    }

                    foreach (var activity in activities) {
                        if (activity.Type == "Ride") {
                            if (activity.Flagged) {
                                this.logger.LogInformation(
                                    "Skipping Activity {ActivityId} for Athlete {AthleteId} because it has been flagged.",
                                    activity.Id,
                                    athlete.Id
                                );
                            } else if (previousUpdates.ContainsKey(activity.Id)) {
                                activitiesSkipped++;
                            } else {
                                var activityDetailsResponse =
                                    await apiHelper.MakeThrottledApiRequest(
                                        () => stravaClient.GetAsync(
                                            $"/api/v3/activities/{activity.Id}?include_all_efforts=true",
                                            cancellationToken),
                                        cancellationToken
                                    );

                                if (activityDetailsResponse.IsSuccessStatusCode) {
                                    var activityDetails =
                                        await activityDetailsResponse.Content
                                            .ReadAsAsync<StravaActivityDetails>(cancellationToken);

                                    var relevantEfforts =
                                        activityDetails.SegmentEfforts
                                            .Where(e => e.Segment.Id == challenge.SegmentId)
                                            .ToList();

                                    if (relevantEfforts.Count > 0) {
                                        // Save Efforts
                                        foreach (var effort in relevantEfforts) {
                                            effortsTable.Add(new Effort {
                                                Id = effort.Id,
                                                AthleteId = athlete.Id,
                                                ActivityId = activity.Id,
                                                SegmentId = challenge.SegmentId,
                                                ElapsedTime = effort.ElapsedTime,
                                                StartDate = effort.StartDate
                                            });
                                        }
                                    }

                                    activityUpdatesTable.Add(
                                        new ActivityUpdate {
                                            ChallengeId = challenge.Id,
                                            ActivityId = activity.Id,
                                            AthleteId = athlete.Id,
                                            UpdateId = update.Id,
                                            UpdatedAt = DateTime.UtcNow
                                        }
                                    );

                                    await dbContext.SaveChangesAsync(cancellationToken);

                                    activitiesUpdated++;
                                    effortsUpdated += relevantEfforts.Count;
                                } else {
                                    logger.LogError(
                                        "An HTTP error occurred attempting to fetch activity details for Athlete {AthleteId} Activity {ActivityId} - Status {StatusCode}: {Content}",
                                        athlete.Id,
                                        activity.Id,
                                        activityDetailsResponse.StatusCode,
                                        await activityDetailsResponse.Content.ReadAsStringAsync()
                                    );

                                    // Give up
                                    return (activitiesUpdated, activitiesSkipped, effortsUpdated, true);
                                }
                            }
                        }
                    }
                } else {
                    logger.LogError(
                        "An HTTP error occurred attempting to fetch activities for Athlete {AthleteId} - Status {StatusCode}: {Content}",
                        athlete.Id,
                        response.StatusCode,
                        await response.Content.ReadAsStringAsync()
                    );

                    // Give up
                    return (activitiesUpdated, activitiesSkipped, effortsUpdated, true);
                }
            }

            return (activitiesUpdated, activitiesSkipped, effortsUpdated, false);
        }
    }

    public class StravaActivity {
        [JsonProperty("id")]
        public Int64 Id { get; set; }

        [JsonProperty("name")]
        public String Name { get; set; }

        [JsonProperty("start_date")]
        public DateTime StartDate { get; set; }

        [JsonProperty("type")]
        public String Type { get; set; }

        [JsonProperty("flagged")]
        public Boolean Flagged { get; set; }
    }

    public class StravaActivityDetails : StravaActivity {
        [JsonProperty("segment_efforts")]
        public StravaEffort[] SegmentEfforts { get; set; }
    }

    public class StravaEffort {
        [JsonProperty("id")]
        public Int64 Id { get; set; }

        [JsonProperty("start_date")]
        public DateTime StartDate { get; set; }

        [JsonProperty("elapsed_time")]
        public Int32 ElapsedTime { get; set; }

        [JsonProperty("segment")]
        public StravaSegment Segment { get; set; }
    }

    public class StravaSegment {
        [JsonProperty("id")]
        public Int64 Id { get; set; }

        [JsonProperty("name")]
        public String Name { get; set; }
    }
}