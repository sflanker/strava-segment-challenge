using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SegmentChallengeWeb.Configuration;
using SegmentChallengeWeb.Models;
using SegmentChallengeWeb.Persistence;

namespace SegmentChallengeWeb.Controllers {
    [ApiController]
    [Route("api/auth")]
    public class LoginController : ControllerBase {
        private readonly IOptions<SegmentChallengeConfiguration> siteConfiguration;
        private readonly IOptions<SegmentChallengeConfiguration> challengeConfiguration;
        private readonly Func<DbConnection> dbConnectionFactory;

        public LoginController(
            IOptions<SegmentChallengeConfiguration> siteConfiguration,
            IOptions<SegmentChallengeConfiguration> challengeConfiguration,
            Func<DbConnection> dbConnectionFactory) {

            this.siteConfiguration = siteConfiguration;
            this.challengeConfiguration = challengeConfiguration;
            this.dbConnectionFactory = dbConnectionFactory;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(AthleteLogin login, CancellationToken cancellationToken) {
            await using var connection = this.dbConnectionFactory();
            await connection.OpenAsync(cancellationToken);

            await using var dbContext = new SegmentChallengeDbContext(connection);

            var athleteTable = dbContext.Set<Athlete>();

            var athlete = await athleteTable.SingleOrDefaultAsync(a => a.Email == login.Email, cancellationToken: cancellationToken);

            if (athlete == null || !BCrypt.Net.BCrypt.Verify(login.Password, athlete.PasswordHash)) {
                // Make em sweat
                await Task.Delay(2000, cancellationToken);
                return Unauthorized();
            } else {
                return ReturnAthleteProfileWithCookie(athlete);
            }
        }

        private IActionResult ReturnAthleteProfileWithCookie(Athlete athlete) {
            Response.Cookies.Append(
                "id_token",
                StravaConnectController.CreateAthleteJwt(
                    this.challengeConfiguration.Value,
                    athlete
                )
            );

            return new JsonResult(new AthleteProfile {
                Username = athlete.Username,
                FirstName = athlete.FirstName,
                LastName = athlete.LastName,
                BirthDate = athlete.BirthDate,
                Gender = athlete.Gender,
                Email = athlete.Email
            });
        }
    }

    public class AthleteLogin {
        public String Email { get; set; }
        public String Password { get; set; }
    }
}