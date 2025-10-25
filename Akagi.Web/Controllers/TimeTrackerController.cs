using Akagi.Web.Data;
using Akagi.Web.Models.TimeTrackers;
using Microsoft.AspNetCore.Mvc;

namespace Akagi.Web.Controllers
{
    [ApiController]
    [Route("TimeTracker")]
    public class TimeTrackerController : ControllerBase
    {
        private readonly IEntryDatabase _entryDatabase;
        private readonly IDefinitionDatabase _definitionDatabase;

        public TimeTrackerController(
            IEntryDatabase entryDatabase,
            IDefinitionDatabase definitionDatabase)
        {
            _entryDatabase = entryDatabase;
            _definitionDatabase = definitionDatabase;
        }
    }
}