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
    public class NotifierClient : DuplexServiceClient<INotifierService>
    {
        public NotifierClient(Binding binding, EndpointAddress endpoint, INotifierServiceCallback callback)
            : base(binding, endpoint, callback)
        {

        }
    }
}
