using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Commands.Base;
using revit_mcp_sdk.API.Base;
using SampleCommandSet.Commands.Code;
using System;
using System.Runtime.InteropServices;

namespace revit_mcp_plugin.Commands.Code
{
    /// <summary>
    /// Command class for handling code execution
    /// </summary>
    public class ExecuteCodeCommand : ExternalEventCommandBase
    {
        private ExecuteCodeEventHandler _handler => (ExecuteCodeEventHandler)Handler;

        public override string CommandName => "send_code_to_revit";

        public ExecuteCodeCommand(UIApplication uiApp)
            : base(new ExecuteCodeEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // Parameter validation
                if (!parameters.ContainsKey("code"))
                {
                    throw new ArgumentException("Missing required parameter: 'code'");
                }

                // Parse code and parameters
                string code = parameters["code"].Value<string>();
                JArray parametersArray = parameters["parameters"] as JArray;
                object[] executionParameters = parametersArray?.ToObject<object[]>() ?? Array.Empty<object>();

                // Set execution parameters
                _handler.SetExecutionParameters(code, executionParameters);

                // Trigger external event and wait for completion
                if (RaiseAndWaitForCompletion(60000)) // 1 minute timeout
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("Code execution timed out");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Code execution failed: {ex.Message}", ex);
            }
        }
    }
}