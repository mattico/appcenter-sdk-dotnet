// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AppCenter.Analytics.Channel;
using Microsoft.AppCenter.Analytics.Ingestion.Models;
using Microsoft.AppCenter.Channel;
using Microsoft.AppCenter.Ingestion.Models.Serialization;
using Microsoft.AppCenter.Utils;
using Microsoft.AppCenter.Windows.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AppCenter.Analytics
{
    public class Analytics : AppCenterService
    {
        #region static

        private const int MaxEventNameLength = 256;

        private static readonly object AnalyticsLock = new object();

        private static volatile Analytics _instanceField;

        // Stores the value of whether automatic session generation value was set, null by default.
        private bool? _isAutomaticSessionGenerationDisabled = null;

        // Internal for testing purposes
        private ISessionTracker _sessionTracker;

        private readonly ISessionTrackerFactory _sessionTrackerFactory;

        public static Analytics Instance
        {
            get
            {
                if (_instanceField != null)
                {
                    return _instanceField;
                }
                lock (AnalyticsLock)
                {
                    return _instanceField ?? (_instanceField = new Analytics());
                }
            }
            set
            {
                lock (AnalyticsLock)
                {
                    _instanceField = value; //for testing
                }
            }
        }

        /// <summary>
        /// Check whether the Analytics service is enabled or not.
        /// </summary>
        /// <returns>A task with result being true if enabled, false if disabled.</returns>
        public static Task<bool> IsEnabledAsync()
        {
            lock (AnalyticsLock)
            {
                return Task.FromResult(Instance.InstanceEnabled);
            }
        }

        /// <summary>
        /// Enable or disable the Analytics service.
        /// </summary>
        /// <returns>A task to monitor the operation.</returns>
        public static Task SetEnabledAsync(bool enabled)
        {
            lock (AnalyticsLock)
            {
                Instance.InstanceEnabled = enabled;
                return Task.FromResult(default(object));
            }
        }

        /// <summary>
        ///     Track a custom event with name and optional properties.
        /// </summary>
        /// <remarks>
        ///     The name parameter can not be null or empty.Maximum allowed length = 256.
        ///     The properties parameter maximum item count = 5.
        ///     The properties keys/names can not be null or empty, maximum allowed key length = 64.
        ///     The properties values can not be null, maximum allowed value length = 64.
        /// </remarks>
        /// <param name="name">An event name.</param>
        /// <param name="properties">Optional properties.</param>
        public static void TrackEvent(string name, IDictionary<string, string> properties = null)
        {
            lock (AnalyticsLock)
            {
                Instance.InstanceTrackEvent(name, properties);
            }
        }

        /// <summary>
        ///  Disable automatic session generation.
        /// </summary>
        /// <param name="isDisabled">True - if automatic session generation should be disabled, otherwise false.</param>
        public static void DisableAutomaticSessionGeneration(bool isDisabled)
        {
            if (Instance.Channel != null)
            {
                AppCenterLog.Error(Instance.LogTag, $"The automatic session generation value should be installed before the App Center start.");
                return;
            }
            if (Instance._sessionTracker == null)
            {
                Instance._isAutomaticSessionGenerationDisabled = isDisabled;
                return;
            }
            Instance._sessionTracker.DisableAutomaticSessionGeneration(isDisabled);
        }

        /// <summary>
        /// Start a new session if automatic session generation was disabled, otherwise nothing.
        /// </summary>
        public static void StartSession()
        {
            Instance._sessionTracker?.StartSession();
        }

        #endregion

        #region instance

        private Analytics()
        {
            LogSerializer.AddLogType(PageLog.JsonIdentifier, typeof(PageLog));
            LogSerializer.AddLogType(EventLog.JsonIdentifier, typeof(EventLog));
            LogSerializer.AddLogType(StartSessionLog.JsonIdentifier, typeof(StartSessionLog));
        }

        internal Analytics(ISessionTrackerFactory sessionTrackerFactory) : this()
        {
            _sessionTrackerFactory = sessionTrackerFactory;
        }

        public override bool InstanceEnabled
        {
            get => base.InstanceEnabled;

            set
            {
                lock (_serviceLock)
                {
                    var prevValue = InstanceEnabled;
                    base.InstanceEnabled = value;
                    if (value != prevValue)
                    {
                        ApplyEnabledState(value);
                    }
                }
            }
        }

        protected override string ChannelName => "analytics";

        public override string ServiceName => "Analytics";

        private void InstanceTrackEvent(string name, IDictionary<string, string> properties = null)
        {
            lock (_serviceLock)
            {
                if (IsInactive)
                {
                    return;
                }
                const string type = "Event";
                if (ValidateName(ref name, type))
                {
                    properties = PropertyValidator.ValidateProperties(properties, $"{type} '{name}'");
                    var log = new EventLog(null, Guid.NewGuid(), name, null, null, null, properties);
                    Channel.EnqueueAsync(log);
                }
            }
        }

        public override void OnChannelGroupReady(IChannelGroup channelGroup, string appSecret)
        {
            lock (_serviceLock)
            {
                base.OnChannelGroupReady(channelGroup, appSecret);
                ApplyEnabledState(InstanceEnabled);
            }
        }

        private void ApplyEnabledState(bool enabled)
        {
            lock (_serviceLock)
            {
                if (enabled && ChannelGroup != null && _sessionTracker == null)
                {
                    _sessionTracker = CreateSessionTracker(ChannelGroup, Channel, ApplicationSettings);
                    if (_isAutomaticSessionGenerationDisabled != null)
                    {
                        _sessionTracker.DisableAutomaticSessionGeneration(_isAutomaticSessionGenerationDisabled.Value);
                    }
                    if (!ApplicationLifecycleHelper.Instance.IsSuspended)
                    {
                        _sessionTracker.Resume();
                    }
                    SubscribeToApplicationLifecycleEvents();
                }
                else if (!enabled)
                {
                    UnsubscribeFromApplicationLifecycleEvents();
                    _sessionTracker?.Stop();
                    _sessionTracker = null;
                }
            }
        }

        private ISessionTracker CreateSessionTracker(IChannelGroup channelGroup, IChannelUnit channel, IApplicationSettings applicationSettings)
        {
            return _sessionTrackerFactory?.CreateSessionTracker(channelGroup, channel, applicationSettings) ?? new SessionTracker(channelGroup, channel);
        }

        /// <summary>
        /// Validates name.
        /// </summary>
        /// <param name="name">Log name to validate.</param>
        /// <param name="logType">Log type.</param>
        /// <returns><c>true</c> if validation succeeds, otherwise <c>false</c>.</returns>
        private bool ValidateName(ref string name, string logType)
        {
            if (string.IsNullOrEmpty(name))
            {
                AppCenterLog.Error(LogTag, $"{logType} name cannot be null or empty.");
                return false;
            }
            if (name.Length > MaxEventNameLength)
            {
                AppCenterLog.Warn(LogTag,
                    $"{logType} '{name}' : name length cannot be longer than {MaxEventNameLength} characters. Name will be truncated.");
                name = name.Substring(0, MaxEventNameLength);
                return true;
            }
            return true;
        }

        private void SubscribeToApplicationLifecycleEvents()
        {
            ApplicationLifecycleHelper.Instance.ApplicationResuming += ApplicationResumingEventHandler;
            ApplicationLifecycleHelper.Instance.ApplicationSuspended += ApplicationSuspendedEventHandler;
        }
        private void UnsubscribeFromApplicationLifecycleEvents()
        {
            ApplicationLifecycleHelper.Instance.ApplicationResuming -= ApplicationResumingEventHandler;
            ApplicationLifecycleHelper.Instance.ApplicationSuspended -= ApplicationSuspendedEventHandler;
        }

        private void ApplicationResumingEventHandler(object sender, EventArgs e)
        {
            _sessionTracker?.Resume();
        }

        private void ApplicationSuspendedEventHandler(object sender, EventArgs e)
        {
            _sessionTracker?.Pause();
        }

        #endregion
    }
}
