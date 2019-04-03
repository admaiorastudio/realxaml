using System;

namespace AdMaiora.RealXaml.Server
{
    public class Program
    {
        static void Main(string[] args)
        {
            using (ViewerServer server = new ViewerServer())
            {
                server.Start();
            }
        }
    }
}
