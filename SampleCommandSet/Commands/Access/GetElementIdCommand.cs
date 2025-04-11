using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Commands.Base;
using revit_mcp_sdk.API.Base;
using System;
using System.Runtime.InteropServices;

namespace revit_mcp_plugin.Commands.Access
{
    /// <summary>
    /// Command to get elements by query string and filter type
    /// </summary>
    public class GetElementIdCommand : ExternalEventCommandBase
    {
        private GetElementIdEventHandler _handler => (GetElementIdEventHandler)Handler;

        public override string CommandName => "get_element_id";

        public GetElementIdCommand(UIApplication uiApp)
            : base(new GetElementIdEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                string query = parameters["query"]?.Value<string>() ?? "";
                string filterType = parameters["filterType"]?.Value<string>() ?? "all";
                int limit = parameters["limit"]?.Value<int>() ?? 100;

                // Set parameters and execute
                _handler.SetParameters(query, filterType, limit);

                if (RaiseAndWaitForCompletion(15000)) // 15-second timeout
                {
                    return _handler.FoundElements;
                }
                else
                {
                    throw new TimeoutException("Element search operation timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to search elements: {ex.Message}");
            }
        }
    }
}