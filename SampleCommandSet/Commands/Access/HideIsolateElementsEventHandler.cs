using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using revit_mcp_plugin.Commands.Interfaces;
using revit_mcp_sdk.API.Interfaces;

namespace revit_mcp_plugin.Commands.Access
{
    /// <summary>
    /// Event handler for hiding or isolating view elements
    /// </summary>
    public class HideIsolateElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Default model category list
        private readonly List<string> _defaultModelCategories = new List<string>
        {
            "OST_Walls",
            "OST_Doors",
            "OST_Windows",
            "OST_Furniture",
            "OST_Columns",
            "OST_Floors",
            "OST_Roofs",
            "OST_Stairs",
            "OST_StructuralFraming",
            "OST_Ceilings",
            "OST_MEPSpaces",
            "OST_Rooms"
        };
        // Default annotation category list
        private readonly List<string> _defaultAnnotationCategories = new List<string>
        {
            "OST_Dimensions",
            "OST_TextNotes",
            "OST_GenericAnnotation",
            "OST_WallTags",
            "OST_DoorTags",
            "OST_WindowTags",
            "OST_RoomTags",
            "OST_AreaTags",
            "OST_SpaceTags",
            "OST_ViewportLabels",
            "OST_TitleBlocks"
        };

        // Query parameters
        private List<long> _elementIds;
        private List<string> _modelCategoryList;
        private List<string> _annotationCategoryList;
        private string _operation;
        private bool _temporary;
        private int _limit;

        // Execution results
        public HideIsolateResult ResultInfo { get; private set; }

        // Status synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Set query parameters
        public void SetQueryParameters(List<long> elementIds, List<string> modelCategoryList, List<string> annotationCategoryList,
                                      string operation, bool temporary, int limit)
        {
            _elementIds = elementIds;
            _modelCategoryList = modelCategoryList;
            _annotationCategoryList = annotationCategoryList;
            _operation = operation;
            _temporary = temporary;
            _limit = limit;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Implement IWaitableExternalEventHandler interface
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;
                var activeView = doc.ActiveView;

                // Get elements to process
                ICollection<ElementId> elementsToProcess = new List<ElementId>();

                // If specific element ID list is provided
                if (_elementIds != null && _elementIds.Count > 0)
                {
                    foreach (var id in _elementIds)
                    {
                        elementsToProcess.Add(new ElementId(id));
                    }
                }
                else
                {
                    // If the input category list is empty, use default lists
                    List<string> modelCategories = (_modelCategoryList == null || _modelCategoryList.Count == 0)
                        ? _defaultModelCategories
                        : _modelCategoryList;

                    List<string> annotationCategories = (_annotationCategoryList == null || _annotationCategoryList.Count == 0)
                        ? _defaultAnnotationCategories
                        : _annotationCategoryList;

                    // Merge all categories
                    List<string> allCategories = new List<string>();
                    allCategories.AddRange(modelCategories);
                    allCategories.AddRange(annotationCategories);

                    // Get all elements from model
                    var collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();

                    // Filter by category
                    if (allCategories.Count > 0)
                    {
                        // Convert string categories to enums
                        List<BuiltInCategory> builtInCategories = new List<BuiltInCategory>();
                        foreach (string categoryName in allCategories)
                        {
                            if (Enum.TryParse(categoryName, out BuiltInCategory category))
                            {
                                builtInCategories.Add(category);
                            }
                        }
                        // If categories were successfully parsed, use category filter
                        if (builtInCategories.Count > 0)
                        {
                            ElementMulticategoryFilter categoryFilter = new ElementMulticategoryFilter(builtInCategories);
                            collector = collector.WherePasses(categoryFilter);
                        }
                    }

                    elementsToProcess = collector.ToElementIds();

                    // Limit element count
                    if (_limit > 0 && elementsToProcess.Count > _limit)
                    {
                        elementsToProcess = elementsToProcess.Take(_limit).ToList();
                    }
                }

                // Apply operation
                using (Transaction trans = new Transaction(doc))
                {
                    trans.Start("Hide/Isolate Elements");
                    int affectedCount = 0;

                    switch (_operation.ToLower())
                    {
                        case "hide":
                            // Hide specified elements
                            activeView.HideElements(elementsToProcess);
                            affectedCount = elementsToProcess.Count;
                            break;

                        case "show":
                            // Show specified elements
                            activeView.UnhideElements(elementsToProcess);
                            affectedCount = elementsToProcess.Count;
                            break;

                        case "isolate":
                            // Isolate specified elements
                            if (_temporary)
                            {
                                // Temporary isolation
                                activeView.IsolateElementsTemporary(elementsToProcess);
                            }
                            else
                            {
                                // Permanent isolation - achieved by hiding all other elements
                                var allElements = new FilteredElementCollector(doc, activeView.Id)
                                    .WhereElementIsNotElementType()
                                    .ToElementIds();

                                // Get elements to hide (all elements not in the isolation list)
                                var elementsToHide = new List<ElementId>();
                                foreach (var id in allElements)
                                {
                                    if (!elementsToProcess.Contains(id))
                                    {
                                        elementsToHide.Add(id);
                                    }
                                }

                                // Hide other elements
                                activeView.HideElements(elementsToHide);
                            }
                            affectedCount = elementsToProcess.Count;
                            break;

                        case "reset":
                            // Reset view (cancel isolation and show all hidden elements)
                            if (_temporary)
                            {
                                // Cancel temporary isolation
                                activeView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                            }
                            else
                            {
                                // Reset all element visibility - by showing all hidden elements
                                var allElements = new FilteredElementCollector(doc, activeView.Id)
                                    .WhereElementIsNotElementType()
                                    .ToElementIds();

                                // Show all elements
                                activeView.UnhideElements(allElements);

                                // Reset graphic overrides for all elements
                                foreach (var id in allElements)
                                {
                                    // Reset graphic override
                                    activeView.SetElementOverrides(id, new OverrideGraphicSettings());
                                }
                            }
                            affectedCount = -1; // Indicates complete reset
                            break;
                    }

                    trans.Commit();

                    // Build result
                    ResultInfo = new HideIsolateResult
                    {
                        ViewId = activeView.Id.Value,
                        ViewName = activeView.Name,
                        Operation = _operation,
                        Temporary = _temporary,
                        AffectedElementsCount = affectedCount,
                        ProcessedElementIds = elementsToProcess.Select(id => id.Value).ToList()
                    };
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
                ResultInfo = new HideIsolateResult
                {
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Hide or Isolate Elements";
        }
    }

    /// <summary>
    /// Hide/Isolate operation result data structure
    /// </summary>
    public class HideIsolateResult
    {
        public long ViewId { get; set; }
        public string ViewName { get; set; }
        public string Operation { get; set; }
        public bool Temporary { get; set; }
        public int AffectedElementsCount { get; set; }
        public List<long> ProcessedElementIds { get; set; } = new List<long>();
        public string ErrorMessage { get; set; }
    }
}