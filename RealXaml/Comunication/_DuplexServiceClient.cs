using AdMaiora.RealXaml.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

namespace AdMaiora.RealXaml.Extension
{
    /// <summary>
    /// Abstract base class for Service Clients wishing to utilise an API
    /// via some service contract channel.
    /// </summary>
    public abstract class DuplexServiceClient<T> : IDisposable where T : class
    {
        #region Constants and Fields

        /// <summary>
        /// Indicates if this instance has been disposed.
        /// </summary>
        private bool _disposed = false;

        #endregion

        #region Properties

        /// <summary>
        /// Provides a derived class access to the API via a dedicated channel.
        /// </summary>
        public T ServiceChannel { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates and configures the ServiceClient and opens the connection
        /// to the API via its Channel.
        /// </summary>
        protected DuplexServiceClient(Binding binding, EndpointAddress endpoint, INotifierServiceCallback callback)
        {
            this.ServiceChannel = DuplexChannelFactory<T>.CreateChannel(new InstanceContext(callback), binding, endpoint);
            (this.ServiceChannel as ICommunicationObject).Open();
        }

        #endregion

        #region Public Methods

        public void ChangeEndpoint(Binding binding, EndpointAddress endpoint)
        {
            var connection = this.ServiceChannel as ICommunicationObject;
            if (connection == null)
                return;

            this.ServiceChannel = ChannelFactory<T>.CreateChannel(binding, endpoint);
        }

        /// <summary>
        /// Closes the client connection.
        /// </summary>
        public void Close()
        {
            (this.ServiceChannel as ICommunicationObject).Close();
        }

        #endregion 

        #region IDisposable Methods

        /// <summary>
        /// Releases held resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Closes the client connection and releases any additional resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (this.ServiceChannel != null)
                {
                    Close();
                    this.ServiceChannel = null;
                    _disposed = true;
                }
            }
        }

        #endregion
    }
}
