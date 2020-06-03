// Copyright (c) Microsoft. All rights reserved.

import React from "react";
import { Toggle } from "@microsoft/azure-iot-ux-fluent-controls/lib/components/Toggle";

import Config from "app.config";
import Flyout from "components/shared/flyout";
import { Btn, Indicator } from "components/shared";
import { svgs, LinkedComponent, isDef } from "utilities";
import { ApplicationSettingsContainer } from "./applicationSettings.container";
import {
    toDiagnosticsModel,
    toSinglePropertyDiagnosticsModel,
} from "services/models";

import "./settings.scss";
import { TenantService } from "services";

const Section = Flyout.Section;

const alertingIsPending = (jobState) => {
    return (
        jobState === "Starting" ||
        jobState === "Stopping" ||
        jobState === "Creating" ||
        jobState === "Deleting"
    );
};

export class Settings extends LinkedComponent {
    constructor(props) {
        super(props);

        this.state = {
            desiredSimulationState: this.props.isSimulationEnabled,
            diagnosticsOptIn: this.props.diagnosticsOptIn,
            logoFile: undefined,
            applicationName: "",
            loading: false,
            toggledSimulation: false,
            madeLogoUpdate: false,
            alertingState: this.props.alerting.jobState,
            alertingIsActive: this.props.alerting.isActive,
            alertingPending: alertingIsPending(this.props.alerting.jobState),
        };

        const { t } = this.props;

        // Helper objects for choosing the correct label for the simulation service toggle input
        this.desiredSimulationLabel = {
            true: t("settingsFlyout.start"),
            false: t("settingsFlyout.stop"),
        };
        this.currSimulationLabel = {
            true: t("settingsFlyout.flowing"),
            false: t("settingsFlyout.stopped"),
        };

        this.applicationName = this.linkTo("applicationName").map((value) =>
            value.length === 0 ? undefined : value
        );

        this.props.getSimulationStatus();

        if (this.state.alertingPending) {
            this.watchAlertingStatusAndUpdate(10);
        }
    }

    componentWillReceiveProps({
        isSimulationEnabled,
        setLogoPending,
        setLogoError,
        getSimulationPending,
        getSimulationError,
        diagnosticsOptIn,
    }) {
        const { madeLogoUpdate, desiredSimulationState } = this.state;
        this.setState({ diagnosticsOptIn: diagnosticsOptIn });
        if (
            !isDef(desiredSimulationState) &&
            isDef(isSimulationEnabled) &&
            !getSimulationPending &&
            !getSimulationError
        ) {
            this.setState({
                desiredSimulationState: isSimulationEnabled,
            });
        }

        if (madeLogoUpdate && !setLogoPending && !setLogoError) {
            this.props.onClose();
        }
    }

    componentDidMount() {
        this.props.logEvent(toDiagnosticsModel("SettingsFlyout_Open", {}));
    }

    onChange = ({ target }) => {
        const { name, value } = target;
        this.setState({ [name]: value });
    };

    onSimulationChange = (value) => {
        const { toggledSimulation } = this.state,
            etag = this.props.simulationEtag;
        this.setState(
            {
                toggledSimulation: true,
                desiredSimulationState: value,
            },
            () => {
                this.props.logEvent(
                    toSinglePropertyDiagnosticsModel(
                        "Settings_SimulationToggle",
                        "isEnabled",
                        toggledSimulation
                    )
                );
            }
        );
        this.props.toggleSimulationStatus(etag, value);
    };

    watchAlertingStatusAndUpdate = (retryCounter) => {
        if (retryCounter === 0) {
            // if the retry counter has reached 0, call it quits, leave the slider where it is but keep it pending
            this.setState({
                alertingPending: true, // leaves the slider unclickable
            });
            return;
        }

        TenantService.getAlertingStatus().subscribe((statusModel) => {
            this.props.updateAlerting(statusModel);
            if (alertingIsPending(statusModel.jobState)) {
                setTimeout(
                    () => this.watchAlertingStatusAndUpdate(retryCounter - 1),
                    // set an extra long timeout for creating state as retrying too soon may cause the request to throw a 404
                    statusModel.jobState === "Creating" ? 30000 : 5000
                );
            } else {
                this.setState({
                    alertingState: statusModel.jobState,
                    alertingPending: false,
                    alertingIsActive: statusModel.isActive,
                });
            }
        });
    };

    onAlertingStatusChange = (value) => {
        //set state of alertingPending to true to disable the button while call is being made
        //set state of alertingState to pending to give user feedback on status of call
        //set alertingIsActive to value as value is the end state once the call is complete
        this.setState({
            alertingPending: true,
            alertingState: this.props.t("settingsFlyout.alertingPending"),
            alertingIsActive: value,
        });

        const maxRetries = 40;

        if (value) {
            TenantService.alertingStart().subscribe(
                (statusModel) => {
                    this.props.logEvent(
                        toSinglePropertyDiagnosticsModel(
                            "Settings_Alerting",
                            value ? "isStarted" : "isStopped",
                            statusModel
                        )
                    );
                    this.setState({
                        alertingState: this.props.t(
                            "settingsFlyout.alertingStarting"
                        ),
                    });
                    this.watchAlertingStatusAndUpdate(maxRetries);
                },
                (err) => {
                    this.setState({
                        alertingPending: false,
                        alertingState: this.props.t(
                            "settingsFlyout.alertingChangeFail"
                        ),
                        alertingIsActive: !value,
                    });
                }
            );
        } else {
            TenantService.alertingStop().subscribe(
                (statusModel) => {
                    this.props.logEvent(
                        toSinglePropertyDiagnosticsModel(
                            "Settings_Alerting",
                            value ? "isStarted" : "isStopped",
                            statusModel
                        )
                    );
                    this.setState({
                        alertingState: this.props.t(
                            "settingsFlyout.alertingStopping"
                        ),
                    });
                    this.watchAlertingStatusAndUpdate(maxRetries);
                },
                (err) => {
                    this.setState({
                        alertingPending: false,
                        alertingState: this.props.t(
                            "settingsFlyout.alertingChangeFail"
                        ),
                        alertingIsActive: !value,
                    });
                }
            );
        }
    };

    onThemeChange = (nextTheme) => {
        this.props.logEvent(
            toSinglePropertyDiagnosticsModel(
                "Settings_ThemeChanged",
                "nextTheme",
                nextTheme
            )
        );
        return this.props.changeTheme(nextTheme);
    };

    onFlyoutClose = (eventName) => {
        this.props.logEvent(toDiagnosticsModel(eventName, {}));
        return this.props.onClose();
    };

    toggleDiagnostics = () => {
        const { diagnosticsOptIn } = this.state;
        this.setState({ diagnosticsOptIn: !diagnosticsOptIn }, () =>
            this.props.updateDiagnosticsOptIn(!diagnosticsOptIn)
        );
    };

    apply = (event) => {
        this.onFlyoutClose.bind(this, "Settings_Apply_Click");
        const { logoFile, applicationName } = this.state;
        if (logoFile || applicationName) {
            let headers = {};
            if (applicationName) {
                headers.name = applicationName;
            }
            if (logoFile) {
                headers["Content-Type"] = logoFile.type;
            } else {
                headers["Content-Type"] = "text/plain";
            }
            this.props.updateLogo(logoFile, headers);
            this.setState({
                madeLogoUpdate: true,
            });
        }
        event.preventDefault();
    };

    onUpload = (file) => {
        this.setState({
            logoFile: file,
        });
        this.props.logEvent(toDiagnosticsModel("Settings_LogoUpdated", {}));
    };

    render() {
        const {
                t,
                theme,
                version,
                releaseNotesUrl,
                isSimulationEnabled,
                simulationToggleError,
                setLogoError,
                getSimulationPending,
                getSimulationError,
                getDiagnosticsPending,
                getDiagnosticsError,
            } = this.props,
            {
                desiredSimulationState,
                alertingIsActive,
                alertingState,
                alertingPending,
                loading,
                logoFile,
                applicationName,
                toggledSimulation,
                madeLogoUpdate,
            } = this.state;
        this.applicationNameLink = this.linkTo("applicationName");
        const hasChanged = logoFile !== undefined || applicationName !== "",
            hasSimulationChanged =
                !getSimulationPending &&
                !getSimulationError &&
                isSimulationEnabled !== desiredSimulationState,
            simulationLabel = hasSimulationChanged
                ? this.desiredSimulationLabel[desiredSimulationState]
                : this.currSimulationLabel[isSimulationEnabled];
        var nextTheme1, nextTheme2;
        if (theme === "light") {
            nextTheme1 = "dark";
            nextTheme2 = "mmm";
        } else if (theme === "dark") {
            nextTheme1 = "light";
            nextTheme2 = "mmm";
        } else if (theme === "mmm") {
            nextTheme1 = "light";
            nextTheme2 = "dark";
        }

        return (
            <Flyout.Container
                header={t("settingsFlyout.title")}
                t={t}
                onClose={this.onFlyoutClose.bind(
                    this,
                    "Settings_TopXClose_Click"
                )}
            >
                <form onSubmit={this.apply}>
                    <div className="settings-workflow-container">
                        <Section.Container collapsable={false}>
                            <Section.Header>
                                {t("settingsFlyout.sendDiagnosticsHeader")}
                            </Section.Header>
                            <Section.Content className="diagnostics-content">
                                {this.props.t(
                                    "settingsFlyout.sendDiagnosticsText"
                                )}
                                <a
                                    href={Config.serviceUrls.privacy}
                                    className="privacy-link"
                                    target="_blank"
                                    rel="noopener noreferrer"
                                >
                                    {this.props.t(
                                        "settingsFlyout.sendDiagnosticsMicrosoftPrivacyUrl"
                                    )}
                                </a>
                                {getDiagnosticsError ? (
                                    <div className="toggle">
                                        {t(
                                            "settingsFlyout.diagnosticsLoadError"
                                        )}
                                    </div>
                                ) : (
                                    <div className="toggle">
                                        <Toggle
                                            name="settings-diagnostics-opt-in"
                                            attr={{
                                                button: {
                                                    "aria-label": t(
                                                        "settingsFlyout.optInButton"
                                                    ),
                                                    type: "button",
                                                },
                                            }}
                                            on={this.state.diagnosticsOptIn}
                                            disabled={getDiagnosticsPending}
                                            onChange={this.toggleDiagnostics}
                                            onLabel={t(
                                                getDiagnosticsPending
                                                    ? "settingsFlyout.loading"
                                                    : "settingsFlyout.sendDiagnosticsCheckbox"
                                            )}
                                            offLabel={t(
                                                getDiagnosticsPending
                                                    ? "settingsFlyout.loading"
                                                    : "settingsFlyout.dontSendDiagnosticsCheckbox"
                                            )}
                                        />
                                    </div>
                                )}
                            </Section.Content>
                        </Section.Container>
                        <Section.Container
                            collapsable={false}
                            className="app-version"
                        >
                            <Section.Header>
                                {t("settingsFlyout.version", { version })}
                            </Section.Header>
                            <Section.Content className="release-notes">
                                <a
                                    href={releaseNotesUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                >
                                    {t("settingsFlyout.viewRelNotes")}
                                </a>
                            </Section.Content>
                        </Section.Container>
                        <Section.Container
                            collapsable={false}
                            className="app-alerting"
                        >
                            <Section.Header>
                                {t("settingsFlyout.alerting")}: {alertingState}
                            </Section.Header>
                            <Section.Content className="release-notes">
                                {t("settingsFlyout.alertingDescription")}
                                <br></br>
                                <br></br>
                                <Toggle
                                    className="alerting-toggle-button"
                                    name={t("settingsFlyout.alertingToggle")}
                                    attr={{
                                        button: {
                                            "aria-label": t(
                                                "settingsFlyout.alertingToggle"
                                            ),
                                            type: "button",
                                        },
                                    }}
                                    on={alertingIsActive}
                                    disabled={alertingPending}
                                    onChange={this.onAlertingStatusChange}
                                    onLabel={
                                        alertingPending
                                            ? t("settingsFlyout.loading")
                                            : t("settingsFlyout.stop")
                                    }
                                    offLabel={
                                        alertingPending
                                            ? t("settingsFlyout.loading")
                                            : t("settingsFlyout.start")
                                    }
                                />
                            </Section.Content>
                        </Section.Container>
                        <Section.Container className="simulation-toggle-container">
                            <Section.Header>
                                {t("settingsFlyout.simulationData")}{" "}
                            </Section.Header>
                            <Section.Content className="simulation-description">
                                {t("settingsFlyout.simulationDescription")}
                                {getSimulationError ? (
                                    <div className="simulation-toggle">
                                        {t(
                                            "settingsFlyout.simulationLoadError"
                                        )}
                                    </div>
                                ) : (
                                    <div className="simulation-toggle">
                                        <Toggle
                                            className="simulation-toggle-button"
                                            name={t(
                                                "settingsFlyout.simulationToggle"
                                            )}
                                            attr={{
                                                button: {
                                                    "aria-label": t(
                                                        "settingsFlyout.simulationToggle"
                                                    ),
                                                    type: "button",
                                                },
                                            }}
                                            on={desiredSimulationState}
                                            disabled={getSimulationPending}
                                            onChange={this.onSimulationChange}
                                            onLabel={
                                                getSimulationPending
                                                    ? t(
                                                          "settingsFlyout.loading"
                                                      )
                                                    : simulationLabel
                                            }
                                            offLabel={
                                                getSimulationPending
                                                    ? t(
                                                          "settingsFlyout.loading"
                                                      )
                                                    : simulationLabel
                                            }
                                        />
                                    </div>
                                )}
                            </Section.Content>
                        </Section.Container>
                        <Section.Container>
                            <Section.Header>
                                {t("settingsFlyout.theme")}
                            </Section.Header>
                            <Section.Content>
                                {t("settingsFlyout.changeTheme")}
                                <button
                                    type="button"
                                    onClick={this.onThemeChange.bind(
                                        this,
                                        nextTheme1
                                    )}
                                    className="toggle-theme-btn"
                                >
                                    {t("settingsFlyout.switchTheme1", {
                                        nextTheme1,
                                    })}
                                </button>
                                <button
                                    type="button"
                                    onClick={this.onThemeChange.bind(
                                        this,
                                        nextTheme2
                                    )}
                                    className="toggle-theme-btn"
                                >
                                    {t("settingsFlyout.switchTheme2", {
                                        nextTheme2,
                                    })}
                                </button>
                            </Section.Content>
                        </Section.Container>
                        <ApplicationSettingsContainer
                            onUpload={this.onUpload}
                            applicationNameLink={this.applicationNameLink}
                            {...this.props}
                        />
                        {toggledSimulation && simulationToggleError && (
                            <div className="toggle-error">
                                {t("settingsFlyout.toggleError")}
                            </div>
                        )}
                        {madeLogoUpdate && setLogoError && (
                            <div className="set-logo-error">
                                {t("settingsFlyout.setLogoError")}
                            </div>
                        )}
                        <div className="btn-container">
                            {!loading && hasChanged && (
                                <Btn
                                    type="submit"
                                    primary={true}
                                    className="apply-button"
                                >
                                    {t("settingsFlyout.apply")}
                                </Btn>
                            )}
                            <Btn
                                type="button"
                                svg={svgs.x}
                                onClick={this.onFlyoutClose.bind(
                                    this,
                                    "Settings_Close_Click"
                                )}
                                className="close-button"
                            >
                                {hasChanged
                                    ? t("settingsFlyout.cancel")
                                    : t("settingsFlyout.close")}
                            </Btn>
                            {loading && <Indicator size="small" />}
                        </div>
                    </div>
                </form>
            </Flyout.Container>
        );
    }
}
