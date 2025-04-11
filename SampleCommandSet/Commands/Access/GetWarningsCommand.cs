using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using revit_mcp_sdk.API.Base;
using System;

namespace SampleCommandSet.Commands.Access
{
    public class GetWarningsCommand : ExternalEventCommandBase
    {
        private GetWarningsEventHandler _handler => (GetWarningsEventHandler)Handler;

        public override string CommandName => "get_warnings";

        /// <param name="uiApp">Revit UIApplication</param>
        public GetWarningsCommand(UIApplication uiApp)
            : base(new GetWarningsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Extract parameters
                var warningTypeFilter = parameters["warningTypeFilter"]?.ToObject<string[]>() ?? new string[0];
                var includeElementIds = parameters["includeElementIds"]?.ToObject<bool>() ?? true;
                var limit = parameters["limit"]?.ToObject<int>() ?? 0;

                // Set parameters for the handler
                _handler.SetParameters(warningTypeFilter, includeElementIds, limit);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(10000)) // 10 second timeout
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("Get warnings operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get warnings: {ex.Message}", ex);
            }
        }
    }
}