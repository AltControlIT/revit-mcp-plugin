using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Commands.Base;
using revit_mcp_sdk.API.Base;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace revit_mcp_plugin.Commands.Access
{
    /// <summary>
    /// Command to hide or isolate elements
    /// </summary>
    public class HideIsolateElementsCommand : ExternalEventCommandBase
    {
        private HideIsolateElementsEventHandler _handler => (HideIsolateElementsEventHandler)Handler;

        public override string CommandName => "hide_isolate_elements";

        public HideIsolateElementsCommand(UIApplication uiApp)
            : base(new HideIsolateElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parse parameters
                List<long> elementIds = parameters?["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
                List<string> modelCategoryList = parameters?["modelCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                List<string> annotationCategoryList = parameters?["annotationCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                string operation = parameters?["operation"]?.Value<string>() ?? "hide";
                bool temporary = parameters?["temporary"]?.Value<bool>() ?? true;
                int limit = parameters?["limit"]?.Value<int>() ?? 1000;

                // Set query parameters
                _handler.SetQueryParameters(elementIds, modelCategoryList, annotationCategoryList, operation, temporary, limit);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(60000)) // 60 seconds timeout
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Timed out while hiding or isolating elements");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to hide or isolate elements: {ex.Message}");
            }
        }
    }
}