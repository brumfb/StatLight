﻿


using System.Diagnostics;

namespace StatLight.Core.Runners
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Composition.Hosting;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using StatLight.Client.Harness.Events;
    using StatLight.Core.Common;
    using StatLight.Core.Common.Abstractions.Timing;
    using StatLight.Core.Configuration;
    using StatLight.Core.Events;
    using StatLight.Core.Events.Aggregation;
    using StatLight.Core.Monitoring;
    using StatLight.Core.Reporting;
    using StatLight.Core.Reporting.Providers.Console;
    using StatLight.Core.Reporting.Providers.TeamCity;
    using StatLight.Core.WebBrowser;
    using StatLight.Core.WebServer;

    public class StatLightRunnerFactory : IStatLightRunnerFactory
    {
        private readonly ILogger _logger;
        private readonly IEventSubscriptionManager _eventSubscriptionManager;
        private readonly IEventPublisher _eventPublisher;
        private BrowserCommunicationTimeoutMonitor _browserCommunicationTimeoutMonitor;
        private ConsoleResultHandler _consoleResultHandler;
        private ExternalComponentFactory _externalComponentFactory;

        public StatLightRunnerFactory(ILogger logger)
            : this(logger, new EventAggregator(logger))
        {
        }

        internal StatLightRunnerFactory(ILogger logger, EventAggregator eventAggregator) : this(logger, eventAggregator, eventAggregator) { }

        public StatLightRunnerFactory(ILogger logger, IEventSubscriptionManager eventSubscriptionManager, IEventPublisher eventPublisher)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            _logger = logger;
            _eventSubscriptionManager = eventSubscriptionManager;
            _eventPublisher = eventPublisher;

            var debugListener = new ConsoleDebugListener(logger);
            _eventSubscriptionManager.AddListener(debugListener);

            var ea = eventSubscriptionManager as EventAggregator;
            if (ea != null)
            {
                ea.IgnoreTracingEvent<InitializationOfUnitTestHarnessClientEvent>();
                ea.IgnoreTracingEvent<TestExecutionClassCompletedClientEvent>();
                ea.IgnoreTracingEvent<TestExecutionClassBeginClientEvent>();
                ea.IgnoreTracingEvent<SignalTestCompleteClientEvent>();
            }

            _externalComponentFactory = new ExternalComponentFactory(_logger);

            SetupExtensions(_eventSubscriptionManager);
        }

        private void SetupExtensions(IEventSubscriptionManager eventSubscriptionManager)
        {
            _externalComponentFactory.LoadUpExtensionsForTestingReportEvents(eventSubscriptionManager);
        }

        public IRunner CreateContinuousTestRunner(StatLightConfiguration statLightConfiguration)
        {
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            IWebServer webServer;
            List<IWebBrowser> webBrowsers;
            IDialogMonitorRunner dialogMonitorRunner;

            BuildAndReturnWebServiceAndBrowser(
                _logger,
                statLightConfiguration,
                out webServer,
                out webBrowsers,
                out dialogMonitorRunner);

            CreateAndAddConsoleResultHandlerToEventAggregator(_logger);

            IRunner runner = new ContinuousConsoleRunner(_logger, _eventSubscriptionManager, _eventPublisher, statLightConfiguration.Server.XapToTestPath, statLightConfiguration.Client, webServer, webBrowsers.First());
            return runner;
        }

        public IRunner CreateTeamCityRunner(StatLightConfiguration statLightConfiguration)
        {
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            ILogger logger = new NullLogger();
            IWebServer webServer;
            List<IWebBrowser> webBrowsers;
            IDialogMonitorRunner dialogMonitorRunner;

            BuildAndReturnWebServiceAndBrowser(
                logger,
                statLightConfiguration,
                out webServer,
                out webBrowsers,
                out dialogMonitorRunner);

            var teamCityTestResultHandler = new TeamCityTestResultHandler(new ConsoleCommandWriter(), statLightConfiguration.Server.XapToTestPath);
            IRunner runner = new TeamCityRunner(logger, _eventSubscriptionManager, _eventPublisher, webServer, webBrowsers, teamCityTestResultHandler, statLightConfiguration.Server.XapToTestPath, dialogMonitorRunner);

            return runner;
        }

        public IRunner CreateOnetimeConsoleRunner(StatLightConfiguration statLightConfiguration)
        {
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            IWebServer webServer;
            List<IWebBrowser> webBrowsers;
            IDialogMonitorRunner dialogMonitorRunner;

            BuildAndReturnWebServiceAndBrowser(
                _logger,
                statLightConfiguration,
                out webServer,
                out webBrowsers,
                out dialogMonitorRunner);

            CreateAndAddConsoleResultHandlerToEventAggregator(_logger);
            IRunner runner = new OnetimeRunner(_logger, _eventSubscriptionManager, _eventPublisher, webServer, webBrowsers, statLightConfiguration.Server.XapToTestPath, dialogMonitorRunner);
            return runner;
        }

        public IRunner CreateWebServerOnlyRunner(StatLightConfiguration statLightConfiguration)
        {
            if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");
            var location = new WebServerLocation(_logger);

            var webServer = CreateWebServer(_logger, statLightConfiguration, location);
            CreateAndAddConsoleResultHandlerToEventAggregator(_logger);
            IRunner runner = new WebServerOnlyRunner(_logger, _eventSubscriptionManager, _eventPublisher, webServer, location.TestPageUrl, statLightConfiguration.Server.XapToTestPath);

            return runner;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal IWebServer CreateWebServer(ILogger logger, StatLightConfiguration statLightConfiguration, WebServerLocation webServerLocation)
        {
            var responseFactory = new ResponseFactory(statLightConfiguration.Server.HostXap, statLightConfiguration.Client);
            var postHandler = new PostHandler(logger, _eventPublisher, statLightConfiguration.Client, responseFactory);

            return new InMemoryWebServer(logger, webServerLocation, responseFactory, postHandler);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private void BuildAndReturnWebServiceAndBrowser(
            ILogger logger,
            StatLightConfiguration statLightConfiguration,
            out IWebServer webServer,
            out List<IWebBrowser> webBrowsers,
            out IDialogMonitorRunner dialogMonitorRunner)
        {

            var location = new WebServerLocation(logger);
            var debugAssertMonitorTimer = new TimerWrapper(statLightConfiguration.Server.DialogSmackDownElapseMilliseconds);
            webServer = CreateWebServer(logger, statLightConfiguration, location);

            webBrowsers = GetWebBrowsers(logger, location.TestPageUrl, statLightConfiguration);

            dialogMonitorRunner = SetupDialogMonitorRunner(logger, webBrowsers, debugAssertMonitorTimer);

            StartupBrowserCommunicationTimeoutMonitor();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "testPageUrlWithQueryString")]
        private List<IWebBrowser> GetWebBrowsers(ILogger logger, Uri testPageUrl, StatLightConfiguration statLightConfiguration)
        {
            var webBrowserFactory = new WebBrowserFactory(logger);

            Func<int, IWebBrowser> webBrowserFactoryHelper;

            if (statLightConfiguration.Server.IsPhoneRun)
            {
                webBrowserFactoryHelper = instanceId =>
                {
                    Func<byte[]> hostXap = statLightConfiguration.Server.HostXap;
                    return _externalComponentFactory.CreatePhone(hostXap);
                };
            }
            else
            {
                var webBrowserType = statLightConfiguration.Client.WebBrowserType;
                var testPageUrlWithQueryString = new Uri(testPageUrl + "?" + statLightConfiguration.Server.QueryString);
                logger.Debug("testPageUrlWithQueryString = " + testPageUrlWithQueryString);
                webBrowserFactoryHelper = instanceId =>
                {
                    return webBrowserFactory.Create(webBrowserType,
                                                    testPageUrlWithQueryString,
                                                    statLightConfiguration.Server.
                                                        ShowTestingBrowserHost,
                                                    statLightConfiguration.Server.
                                                        ForceBrowserStart,
                                                    statLightConfiguration.Client.
                                                        NumberOfBrowserHosts > 1);
                };
            }

            return Enumerable
                .Range(1, statLightConfiguration.Client.NumberOfBrowserHosts)
                .Select(browserI => webBrowserFactoryHelper(browserI))
                .ToList();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private void StartupBrowserCommunicationTimeoutMonitor()
        {
            if (_browserCommunicationTimeoutMonitor == null)
            {
                _browserCommunicationTimeoutMonitor = new BrowserCommunicationTimeoutMonitor(_eventPublisher, new TimerWrapper(3000), TimeSpan.FromMinutes(5));
                _eventSubscriptionManager.AddListener(_browserCommunicationTimeoutMonitor);
            }
        }

        private void CreateAndAddConsoleResultHandlerToEventAggregator(ILogger logger)
        {
            if (_consoleResultHandler == null)
            {
                _consoleResultHandler = new ConsoleResultHandler(logger);
                _eventSubscriptionManager.AddListener(_consoleResultHandler);
            }
        }

        //[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        //public IRunner CreateRemotelyHostedRunner(StatLightConfiguration statLightConfiguration)
        //{
        //    if (statLightConfiguration == null) throw new ArgumentNullException("statLightConfiguration");

        //    ClientTestRunConfiguration clientTestRunConfiguration = statLightConfiguration.Client;
        //    ServerTestRunConfiguration serverTestRunConfiguration = statLightConfiguration.Server;

        //    throw new NotImplementedException();
        //    //var urlToTestPage = statLightConfiguration.Client.XapToTestUrl.ToUri();

        //    //var location = new RemoteSiteOverriddenLocation(logger, urlToTestPage);
        //    //var debugAssertMonitorTimer = new TimerWrapper(serverTestRunConfiguration.DialogSmackDownElapseMilliseconds);
        //    //SetupDebugClientEventListener(logger);
        //    //var webServer = CreateWebServer(logger, statLightConfiguration, location);
        //    //
        //    //var showTestingBrowserHost = serverTestRunConfiguration.ShowTestingBrowserHost;
        //    //
        //    //var querystring = "?{0}={1}".FormatWith(StatLightServiceRestApi.StatLightResultPostbackUrl,
        //    //                                       HttpUtility.UrlEncode(location.BaseUrl.ToString()));
        //    //var testPageUrlAndPostbackQuerystring = new Uri(location.TestPageUrl + querystring);
        //    //logger.Debug("testPageUrlAndPostbackQuerystring={0}".FormatWith(testPageUrlAndPostbackQuerystring.ToString()));
        //    //var webBrowsers = GetWebBrowsers(logger, testPageUrlAndPostbackQuerystring, clientTestRunConfiguration, showTestingBrowserHost, serverTestRunConfiguration.QueryString, statLightConfiguration.Server.ForceBrowserStart);
        //    //
        //    //var dialogMonitorRunner = SetupDialogMonitorRunner(logger, webBrowsers, debugAssertMonitorTimer);
        //    //
        //    //StartupBrowserCommunicationTimeoutMonitor();
        //    //CreateAndAddConsoleResultHandlerToEventAggregator(logger);
        //    //
        //    //IRunner runner = new OnetimeRunner(logger, _eventSubscriptionManager, _eventPublisher, webServer, webBrowsers, statLightConfiguration.Server.XapToTestPath, dialogMonitorRunner);
        //    //return runner;
        //}

        private IDialogMonitorRunner SetupDialogMonitorRunner(ILogger logger, List<IWebBrowser> webBrowsers, TimerWrapper debugAssertMonitorTimer)
        {
            var dialogMonitors = new List<IDialogMonitor>
                                     {
                                         new DebugAssertMonitor(logger),
                                     };

            foreach (var webBrowser in webBrowsers)
            {
                var monitor = new MessageBoxMonitor(logger, webBrowser);
                dialogMonitors.Add(monitor);
            }

            return new DialogMonitorRunner(logger, _eventPublisher, debugAssertMonitorTimer, dialogMonitors);
        }
    }
}
