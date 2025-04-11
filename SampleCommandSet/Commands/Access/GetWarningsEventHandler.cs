using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using revit_mcp_sdk.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq; // Add this missing namespace
using System.Threading;

namespace SampleCommandSet.Commands.Access
{
    public class GetWarningsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;

        // Parameters
        private string[] _warningTypeFilter;
        private bool _includeElementIds;
        private int _limit;

        /// <summary>
        /// Event wait object
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Execution result
        /// </summary>
        public object Result { get; private set; }

        /// <summary>
        /// Set parameters for the operation
        /// </summary>
        public void SetParameters(string[] warningTypeFilter, bool includeElementIds, int limit)
        {
            _warningTypeFilter = warningTypeFilter;
            _includeElementIds = includeElementIds;
            _limit = limit;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication app)
        {
            uiApp = app;

            try
            {
                if (doc == null)
                {
                    Result = new
                    {
                        success = false,
                        message = "No active document found."
                    };
                    return;
                }

                // Get all warnings from the document
                var warnings = doc.GetWarnings();

                // Filter warnings if filters are provided
                IList<FailureMessage> filteredWarnings = warnings;
                if (_warningTypeFilter != null && _warningTypeFilter.Length > 0)
                {
                    filteredWarnings = warnings.Where(w =>
                        _warningTypeFilter.Any(filter =>
                            w.GetDescriptionText().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        )
                    ).ToList();
                }

                // Limit results if requested
                if (_limit > 0 && filteredWarnings.Count > _limit)
                {
                    filteredWarnings = filteredWarnings.Take(_limit).ToList();
                }

                // Group warnings by description
                var warningGroups = filteredWarnings
                    .GroupBy(w => w.GetDescriptionText())
                    .Select(g => new
                    {
                        Description = g.Key,
                        Count = g.Count(),
                        Severity = g.First().GetSeverity().ToString(),
                        TotalFailingElementCount = g.Sum(w => w.GetFailingElements().Count),
                        Warnings = _includeElementIds ? g.Select(w => new
                        {
                            Description = w.GetDescriptionText(),
                            FailingElements = w.GetFailingElements().Select(id =>
                            {
                                Element element = doc.GetElement(id);
                                return new
                                {
#if REVIT2024_OR_GREATER
                                    Id = id.Value.ToString(),
#else
                                    Id = id.IntegerValue.ToString(),
#endif
                                    Category = element?.Category?.Name ?? "Unknown",
                                    Name = element?.Name ?? "Unknown",
                                    TypeName = GetElementTypeName(element)
                                };
                            }).ToList()
                        }).ToList() : null
                    })
                    .OrderByDescending(g => g.Count)
                    .ToList();

                // Prepare the successful result
                Result = new
                {
                    success = true,
                    ModelName = doc.Title,
                    TotalWarningCount = warnings.Count,
                    FilteredWarningCount = filteredWarnings.Count,
                    WarningGroups = warningGroups
                };
            }
            catch (Exception ex)
            {
                // Prepare error result
                Result = new
                {
                    success = false,
                    message = $"Error retrieving warnings: {ex.Message}"
                };
            }
            finally
            {
                // Signal completion
                _resetEvent.Set();
            }
        }

        /// <summary>
        /// Wait for the operation to complete
        /// </summary>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// Get handler name
        /// </summary>
        public string GetName()
        {
            return "Get Warnings";
        }

        /// <summary>
        /// Get the element type name
        /// </summary>
        private string GetElementTypeName(Element element)
        {
            if (element == null)
                return "Unknown";

            // Try to get the element type name
            try
            {
                ElementId typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element elementType = doc.GetElement(typeId);
                    if (elementType != null)
                    {
                        return elementType.Name;
                    }
                }

                // If no type could be determined, return the element's name
                return element.Name;
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}