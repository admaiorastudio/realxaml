using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace AdMaiora.RealXaml.Server.Controllers
{    
    [Route("api/[controller]")]
    [ApiController]
    public class CommandController : ControllerBase
    {
        private MessageHub _hub;

        public CommandController(MessageHub hub)
        {
            _hub = hub;
        }
    }
}
