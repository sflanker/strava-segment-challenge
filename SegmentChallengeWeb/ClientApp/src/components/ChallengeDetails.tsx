import * as React from 'react';
import {connect, Matching} from 'react-redux';
import {RouteComponentProps} from "react-router";
import {Link} from "react-router-dom";
import moment from "moment";
import {ApplicationState} from "../store";
import {Challenge} from "../store/ChallengeList";
import * as ChallengeDetailStore from "../store/ChallengeDetails"
import * as ChallengeListStore from "../store/ChallengeList"
import JoinButton from "./JoinButton";
import EffortList from "./EffortList";
import UploadChallengeGpx from "./UploadChallengeGpx";
import CategorySelector from "./CategorySelector";
import {LoginState} from "../store/Login";
import {Category} from "../store/ChallengeDetails";
import NoEffortList from "./NoEffortList";
import UploadEffortGpx from "./UploadEffortGpx";

type ChallengeDetailsProps =
    ChallengeDetailStore.ChallengeDetailsState &
    ChallengeListStore.ChallengeListState &
    { login?: LoginState } &
    {
        onSelectedChallengeChanged: (selectedChallenge: string) => void,
        selectedCategoryChanged: (selectedCategory: Category) => ChallengeDetailStore.SelectedCategoryChanged
    } &
    RouteComponentProps<{ challengeName: string }>;

type ChallengeDetailsState = { bestEffort?: number }

class ChallengeDetails extends React.PureComponent<Matching<ChallengeDetailsProps, ChallengeDetailsProps>, ChallengeDetailsState> {
    constructor(props: ChallengeDetailsProps) {
        super(props);

        this.state = {};
    }

    public componentDidMount() {
        this.props.onSelectedChallengeChanged(this.props.match.params.challengeName);
    }

    public componentDidUpdate() {
        this.props.onSelectedChallengeChanged(this.props.match.params.challengeName);

        if (this.props.allEfforts && this.props.login && this.props.login.loggedInUser) {
            const loggedInUser = this.props.login.loggedInUser;
            this.setState({
                bestEffort: this.props.allEfforts.filter(e => e.athleteId === Number(loggedInUser.sub))[0]?.id
            })
        }
    }

    public render() {
        return (
            <React.Fragment>
                {this.props.requestError &&
                <span className="error-message">{this.props.requestError}</span>}
                {this.props.currentChallenge ?
                    this.renderChallengeDetails() :
                    (this.props.challenges ? ChallengeDetails.renderNotFound() : ChallengeDetails.renderLoadingIndicator())}
            </React.Fragment>
        );
    }

    private renderChallengeDetails() {
        return this.props.currentChallenge && (
            <div>
                <div id="challenge_title">
                    <h2><a id="strava_segment_link" href={`https://www.strava.com/segments/${this.props.currentChallenge?.segmentId}`} target="_blank" title="View Segment on Strava">{this.props.currentChallenge.displayName}</a></h2>
                    {(this.props.isAthleteRegistered === false) && <JoinButton />}
                    {this.state.bestEffort ?
                        <Link to={({pathname: this.props.location.pathname, hash: `effort_${this.state.bestEffort}`})}
                              className="effort-link">
                            Your Effort
                        </Link> :
                        (this.props.isAthleteRegistered === true && <span>You have joined. Check back later for your efforts.</span>)}
                </div>
                <h3>{this.props.selectedCategory.description}</h3>
                <div className="flex-row row">
                    <EffortList />
                    {/*<div className="side-panel">*/}
                    {/*    <CategorySelector />*/}
                    {/*</div>*/}
                </div>
                <div className="row">
                    <NoEffortList selectedCategory={this.props.selectedCategory} />
                </div>
                {this.props.login?.loggedInUser?.user_data.is_admin && <UploadChallengeGpx selectedCategory={this.props.selectedCategory} />}
                <UploadEffortGpx selectedCategory={this.props.selectedCategory} />
            </div>
        );
    }

    private static renderLoadingIndicator() {
        return (
            <div className="loading-indicator">Loading ...</div>
        )
    }

    private static renderNotFound() {

        return (
            <div>
                <span className="error-message">The Challenge you are looking for was not found.</span>
            </div>
        )
    }

    private static getSelectedChallenge(challenges: Challenge[], challengeName: string): Challenge | undefined {
        return challenges.filter(c => c.name === challengeName)[0];
    }
}

export default connect(
    (state: ApplicationState) => ({...state.challengeList, ...state.challengeDetails, login: state.login}),
    ChallengeDetailStore.actionCreators
)(ChallengeDetails);
