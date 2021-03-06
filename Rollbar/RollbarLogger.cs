﻿[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("UnitTest.Rollbar")]

namespace Rollbar
{
    using Rollbar.Diagnostics;
    using Rollbar.DTOs;
    using Rollbar.Telemetry;
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Implements disposable implementation of IRollbar.
    /// 
    /// All the logging methods implemented in async "fire-and-forget" fashion.
    /// Hence, the payload is not yet delivered to the Rollbar API service when
    /// the methods return.
    /// 
    /// </summary>
    /// <seealso cref="Rollbar.IRollbar" />
    /// <seealso cref="System.IDisposable" />
    internal class RollbarLogger
        : IRollbar
        , IDisposable
    {

        private readonly IRollbarConfig _config;
        private readonly PayloadQueue _payloadQueue;

        /// <summary>
        /// Occurs when a Rollbar internal event happens.
        /// </summary>
        public event EventHandler<RollbarEventArgs> InternalEvent;

        /// <summary>
        /// Initializes a new instance of the <see cref="RollbarLogger"/> class.
        /// </summary>
        /// <param name="isSingleton">if set to <c>true</c> [is singleton].</param>
        internal RollbarLogger(bool isSingleton)
            : this(isSingleton, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RollbarLogger"/> class.
        /// </summary>
        /// <param name="isSingleton">if set to <c>true</c> [is singleton].</param>
        /// <param name="rollbarConfig">The rollbar configuration.</param>
        internal RollbarLogger(bool isSingleton, IRollbarConfig rollbarConfig)
        {
            if (!TelemetryCollector.Instance.IsAutocollecting)
            {
                TelemetryCollector.Instance.StartAutocollection();
            }

            this.IsSingleton = isSingleton;

            if (rollbarConfig != null)
            {
                this._config = rollbarConfig;
            }
            else
            {
                this._config = new RollbarConfig(this);
            }

            var rollbarClient = new RollbarClient(
                this._config
                , RollbarQueueController.Instance.ProvideHttpClient(
                    this._config.ProxyAddress, 
                    this._config.ProxyUsername, 
                    this._config.ProxyPassword
                    )
                );

            this._payloadQueue = new PayloadQueue(this, rollbarClient);
            RollbarQueueController.Instance.Register(this._payloadQueue);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is singleton.
        /// </summary>
        /// <value><c>true</c> if this instance is singleton; otherwise, <c>false</c>.</value>
        internal bool IsSingleton { get; private set; }

        /// <summary>
        /// Gets the queue.
        /// </summary>
        /// <value>The queue.</value>
        internal PayloadQueue Queue
        {
            get { return this._payloadQueue; }
        }

        #region IRollbar

        /// <summary>
        /// Gets the logger.
        /// </summary>
        /// <value>The logger.</value>
        public ILogger Logger => this;

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        public IRollbarConfig Config
        {
            get { return this._config; }
        }

        /// <summary>
        /// Configures the using specified settings.
        /// </summary>
        /// <param name="settings">The settings.</param>
        /// <returns>IRollbar.</returns>
        public IRollbar Configure(IRollbarConfig settings)
        {
            this._config.Reconfigure(settings);

            return this;
        }

        /// <summary>
        /// Configures using the specified access token.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>IRollbar.</returns>
        public IRollbar Configure(string accessToken)
        {
            return this.Configure(new RollbarConfig(accessToken));
        }

        #endregion IRollbar

        #region ILogger

        /// <summary>
        /// Returns blocking/synchronous implementation of this ILogger.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>Blocking (fully synchronous) instance of an ILogger.
        /// It either completes logging calls within the specified timeout
        /// or throws a TimeoutException.</returns>
        public ILogger AsBlockingLogger(TimeSpan timeout)
        {
            return new RollbarLoggerBlockingWrapper(this, timeout);
        }

        /// <summary>
        /// Logs using the specified level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Log(ErrorLevel level, object obj)
        {
            return this.Log(level, obj, null);
        }

        /// <summary>
        /// Logs the specified object as critical.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Critical(object obj)
        {
            return this.Critical(obj, null);
        }

        /// <summary>
        /// Logs the specified object as error.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Error(object obj)
        {
            return this.Error(obj, null);
        }

        /// <summary>
        /// Logs the specified object as warning.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Warning(object obj)
        {
            return this.Warning(obj, null);
        }

        /// <summary>
        /// Logs the specified object as info.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Info(object obj)
        {
            return this.Info(obj, null);
        }

        /// <summary>
        /// Logs the specified object as debug.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Debug(object obj)
        {
            return this.Debug(obj, null);
        }

        /// <summary>
        /// Logs the specified rollbar data.
        /// </summary>
        /// <param name="rollbarData">The rollbar data.</param>
        /// <returns>ILogger.</returns>
        public ILogger Log(DTOs.Data rollbarData)
        {
            return this.Enqueue(rollbarData, rollbarData.Level.HasValue ? rollbarData.Level.Value : ErrorLevel.Debug, null);
        }

        /// <summary>
        /// Logs using the specified level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Log(ErrorLevel level, object obj, IDictionary<string, object> custom)
        {
            return this.Enqueue(obj, level, custom);
        }


        /// <summary>
        /// Logs the specified object as critical.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Critical(object obj, IDictionary<string, object> custom)
        {
            return this.Enqueue(obj, ErrorLevel.Critical, custom);
        }

        /// <summary>
        /// Logs the specified object as error.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Error(object obj, IDictionary<string, object> custom)
        {
            return this.Enqueue(obj, ErrorLevel.Error, custom);
        }

        /// <summary>
        /// Logs the specified object as warning.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Warning(object obj, IDictionary<string, object> custom)
        {
            return this.Enqueue(obj, ErrorLevel.Warning, custom);
        }

        /// <summary>
        /// Logs the specified object as info.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Info(object obj, IDictionary<string, object> custom)
        {
            return this.Enqueue(obj, ErrorLevel.Info, custom);
        }

        /// <summary>
        /// Logs the specified object as debug.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        public ILogger Debug(object obj, IDictionary<string, object> custom)
        {
            return this.Enqueue(obj, ErrorLevel.Debug, custom);
        }

        #endregion ILogger

        #region IRollbar explicitly

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        IRollbarConfig IRollbar.Config { get { return this.Config; } }

        /// <summary>
        /// Gets the logger.
        /// </summary>
        /// <value>The logger.</value>
        ILogger IRollbar.Logger { get { return this; } }

        /// <summary>
        /// Configures the using specified settings.
        /// </summary>
        /// <param name="settings">The settings.</param>
        /// <returns>IRollbar.</returns>
        IRollbar IRollbar.Configure(IRollbarConfig settings)
        {
            return this.Configure(settings);
        }

        /// <summary>
        /// Configures using the specified access token.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>IRollbar.</returns>
        IRollbar IRollbar.Configure(string accessToken)
        {
            return this.Configure(accessToken);
        }

        /// <summary>
        /// Occurs when a Rollbar internal event happens.
        /// </summary>
        event EventHandler<RollbarEventArgs> IRollbar.InternalEvent
        {
            add
            {
                this.InternalEvent += value;
            }

            remove
            {
                this.InternalEvent -= value;
            }
        }

        #endregion IRollbar explicitly

        #region ILogger explicitly

        /// <summary>
        /// Returns blocking/synchronous implementation of this ILogger.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>Blocking (fully synchronous) instance of an ILogger.
        /// It either completes logging calls within the specified timeout
        /// or throws a TimeoutException.</returns>
        ILogger ILogger.AsBlockingLogger(TimeSpan timeout)
        {
            return this.AsBlockingLogger(timeout);
        }

        /// <summary>
        /// Logs the specified rollbar data.
        /// </summary>
        /// <param name="rollbarData">The rollbar data.</param>
        /// <returns>ILogger.</returns>
        ILogger ILogger.Log(Data rollbarData)
        {
            return this.Log(rollbarData);
        }

        /// <summary>
        /// Logs using the specified level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Log(ErrorLevel level, object obj)
        {
            return this.Log(level, obj);
        }

        /// <summary>
        /// Logs the specified object as critical.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Critical(object obj)
        {
            return this.Critical(obj);
        }

        /// <summary>
        /// Logs the specified object as error.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Error(object obj)
        {
            return this.Error(obj);
        }

        /// <summary>
        /// Logs the specified object as warning.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Warning(object obj)
        {
            return this.Warning(obj);
        }

        /// <summary>
        /// Logs the specified object as info.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Info(object obj)
        {
            return this.Info(obj);
        }

        /// <summary>
        /// Logs the specified object as debug.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Debug(object obj)
        {
            return this.Debug(obj);
        }

        /// <summary>
        /// Logs using the specified level.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Log(ErrorLevel level, object obj, IDictionary<string, object> custom)
        {
            return this.Log(level, obj, custom);
        }


        /// <summary>
        /// Logs the specified object as critical.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Critical(object obj, IDictionary<string, object> custom)
        {
            return this.Critical(obj, custom);
        }

        /// <summary>
        /// Logs the specified object as error.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Error(object obj, IDictionary<string, object> custom)
        {
            return this.Error(obj, custom);
        }

        /// <summary>
        /// Logs the specified object as warning.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Warning(object obj, IDictionary<string, object> custom)
        {
            return this.Warning(obj, custom);
        }

        /// <summary>
        /// Logs the specified object as info.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Info(object obj, IDictionary<string, object> custom)
        {
            return this.Info(obj, custom);
        }

        /// <summary>
        /// Logs the specified object as debug.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="custom">The custom data.</param>
        /// <returns>Instance of the same ILogger that was used for this call.</returns>
        ILogger ILogger.Debug(object obj, IDictionary<string, object> custom)
        {
            return this.Debug(obj, custom);
        }

        #endregion ILogger explicitly 

        #region IDisposable explicitly

        void IDisposable.Dispose()
        {
            this.Dispose();
        }

        #endregion IDisposable explicitly

        internal ILogger Enqueue(
            object dataObject,
            ErrorLevel level,
            IDictionary<string, object> custom,
            TimeSpan? timeout = null,
            SemaphoreSlim signal = null
            )
        {
            // here is the last chance to decide if we need to actually send this payload
            // based on the current config settings:
            if (string.IsNullOrWhiteSpace(this._config.AccessToken)
                || this._config.Enabled == false
                || (this._config.LogLevel.HasValue && level < this._config.LogLevel.Value)
                )
            {
                // nice shortcut:
                return this;
            }

            DateTime? timeoutAt = null;
            if (timeout.HasValue)
            {
                timeoutAt = DateTime.Now.Add(timeout.Value);
            }

            PayloadBundle payloadBundle = null;

            IRollbarPackage rollbarPackage = dataObject as IRollbarPackage;
            if (rollbarPackage != null)
            {
                if (rollbarPackage.MustApplySynchronously)
                {
                    rollbarPackage.PackageAsRollbarData();
                }
                payloadBundle =
                    new PayloadBundle(this.Config, rollbarPackage, level, custom, timeoutAt, signal);
            }
            else
            {
                payloadBundle =
                    new PayloadBundle(this.Config, dataObject, level, custom, timeoutAt, signal);
            }

            if (payloadBundle == null)
            {
                //TODO: we may want to report that there is some problem with packaging...
                return this;
            }

            this._payloadQueue.Enqueue(payloadBundle);

            return this;
        }

        internal virtual void OnRollbarEvent(RollbarEventArgs e)
        {
            EventHandler<RollbarEventArgs> handler = InternalEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    this._payloadQueue.Release();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~RollbarLogger() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <remarks>
        /// This code added to correctly implement the disposable pattern.
        /// </remarks>
        public void Dispose()
        {
            // RollbarLogger type supports both paradigms: singleton-like (via RollbarLocator) and
            // multiple disposable instances (via RollbarFactory).
            // Here we want to make sure that the singleton instance is never disposed:
            Assumption.AssertTrue(!this.IsSingleton, nameof(this.IsSingleton));

            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }

        #endregion IDisposable Support

    }
}
