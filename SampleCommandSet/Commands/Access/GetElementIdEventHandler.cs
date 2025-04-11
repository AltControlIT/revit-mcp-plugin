using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using revit_mcp_plugin.Commands.Interfaces;
using revit_mcp_plugin.Models;
using revit_mcp_sdk.API.Interfaces;
using SampleCommandSet.Commands.Access;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace revit_mcp_plugin.Commands.Access
{
    public class GetElementIdEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution parameters
        private string _query;
        private string _filterType;
        private int _limit;

        // Execution results
        public List<ElementInfo> FoundElements { get; private set; }

        // Status synchronization
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        public bool TaskCompleted { get; private set; }

        // Set search parameters
        public void SetParameters(string query, string filterType, int limit)
        {
            _query = query?.ToLower() ?? "";
            _filterType = filterType?.ToLower() ?? "all";
            _limit = limit > 0 ? limit : 100;

            TaskCompleted = false;
            _resetEvent.Reset();
            FoundElements = new List<ElementInfo>();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                FoundElements = new List<ElementInfo>();

                // Get all elements in the document
                var collector = new FilteredElementCollector(doc);
                IList<Element> allElements;

                // Apply category filter if specified
                if (_filterType == "walls")
                {
                    allElements = collector.OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "doors")
                {
                    allElements = collector.OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "windows")
                {
                    allElements = collector.OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "furniture")
                {
                    allElements = collector.OfCategory(BuiltInCategory.OST_Furniture).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "scopebox" || _filterType == "volumeofinterest")
                {
                    // Add specific support for scope boxes / volumes of interest
                    allElements = collector.OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "levels")
                {
                    allElements = collector.OfCategory(BuiltInCategory.OST_Levels).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "views")
                {
                    allElements = collector.OfClass(typeof(View)).WhereElementIsNotElementType().ToElements();
                }
                else if (_filterType == "grids")
                {
                    allElements = collector.OfCategory(BuiltInCategory.OST_Grids).WhereElementIsNotElementType().ToElements();
                }
                else // "all" or any invalid value
                {
                    allElements = collector.WhereElementIsNotElementType().ToElements();
                }

                // Search for elements matching the query
                foreach (Element element in allElements)
                {
                    // Skip elements that have been deleted
                    if (element.Id == ElementId.InvalidElementId)
                        continue;

                    // Check for match in name
                    if (!string.IsNullOrEmpty(_query))
                    {
                        bool isMatch = false;

                        // Check element name
                        if (element.Name != null && element.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatch = true;
                        }

                        // Check element category
                        Category category = element.Category;
                        if (!isMatch && category != null && category.Name != null &&
                            category.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isMatch = true;
                        }

                        // Check element parameters for matches
                        if (!isMatch)
                        {
                            foreach (Parameter param in element.Parameters)
                            {
                                if (param.HasValue && param.StorageType == StorageType.String)
                                {
                                    string value = param.AsString();
                                    if (!string.IsNullOrEmpty(value) && value.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isMatch = true;
                                        break;
                                    }
                                }
                            }
                        }

                        // If not a match, skip this element
                        if (!isMatch)
                            continue;
                    }

                    // Element matched, add to results
                    var result = new ElementInfo
                    {
                        Id = element.Id.IntegerValue,
                        UniqueId = element.UniqueId,
                        Name = element.Name ?? "Unnamed",
                        Category = element.Category?.Name ?? "Unknown",
                        Properties = GetElementProperties(element)
                    };

                    FoundElements.Add(result);

                    // Check if we've reached the limit
                    if (FoundElements.Count >= _limit)
                        break;
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Search elements failed: {ex.Message}");
                FoundElements = new List<ElementInfo>();
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private Dictionary<string, string> GetElementProperties(Element element)
        {
            var properties = new Dictionary<string, string>();

            // Add common properties
            properties["ElementId"] = element.Id.IntegerValue.ToString();

            if (element.Location != null)
            {
                if (element.Location is LocationPoint locationPoint)
                {
                    var point = locationPoint.Point;
                    properties["LocationX"] = point.X.ToString("F2");
                    properties["LocationY"] = point.Y.ToString("F2");
                    properties["LocationZ"] = point.Z.ToString("F2");
                }
                else if (element.Location is LocationCurve locationCurve)
                {
                    var curve = locationCurve.Curve;
                    properties["Start"] = $"{curve.GetEndPoint(0).X:F2}, {curve.GetEndPoint(0).Y:F2}, {curve.GetEndPoint(0).Z:F2}";
                    properties["End"] = $"{curve.GetEndPoint(1).X:F2}, {curve.GetEndPoint(1).Y:F2}, {curve.GetEndPoint(1).Z:F2}";
                    properties["Length"] = curve.Length.ToString("F2");
                }
            }

            // Get basic parameters
            var commonParams = new[] { "Comments", "Mark", "Level", "Family", "Type" };
            foreach (var paramName in commonParams)
            {
                Parameter param = element.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly)
                {
                    if (param.StorageType == StorageType.String)
                        properties[paramName] = param.AsString() ?? "";
                    else if (param.StorageType == StorageType.Double)
                        properties[paramName] = param.AsDouble().ToString("F2");
                    else if (param.StorageType == StorageType.Integer)
                        properties[paramName] = param.AsInteger().ToString();
                    else if (param.StorageType == StorageType.ElementId)
                        properties[paramName] = param.AsElementId().IntegerValue.ToString();
                }
            }

            // Get additional parameters for scope boxes
            if (element.Category != null &&
                (element.Category.Name == "Volumes" || element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_VolumeOfInterest))
            {
                // Add all parameters for scope boxes
                foreach (Parameter param in element.Parameters)
                {
                    if (param.HasValue && !properties.ContainsKey(param.Definition.Name))
                    {
                        string value = "";

                        if (param.StorageType == StorageType.String)
                            value = param.AsString() ?? "";
                        else if (param.StorageType == StorageType.Double)
                            value = param.AsDouble().ToString("F2");
                        else if (param.StorageType == StorageType.Integer)
                            value = param.AsInteger().ToString();
                        else if (param.StorageType == StorageType.ElementId)
                            value = param.AsElementId().IntegerValue.ToString();

                        properties[param.Definition.Name] = value;
                    }
                }
            }

            return properties;
        }

        public string GetName()
        {
            return "Get Element By Name";
        }
    }
}