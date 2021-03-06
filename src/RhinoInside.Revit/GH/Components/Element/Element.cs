using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using DB = Autodesk.Revit.DB;

namespace RhinoInside.Revit.GH.Components
{
  public abstract class ElementGetter : Component
  {
    public override GH_Exposure Exposure => GH_Exposure.primary;
    protected static readonly Type ObjectType = typeof(Types.Element);

    protected ElementGetter(string propertyName)
      : base(ObjectType.Name + "." + propertyName, propertyName, "Get the " + propertyName + " of the specified " + ObjectType.Name, "Revit", ObjectType.Name)
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), ObjectType.Name, ObjectType.Name.Substring(0, 1), ObjectType.Name + " to query", GH_ParamAccess.item);
    }
  }

  public class ElementIdentity : Component
  {
    public override Guid ComponentGuid => new Guid("D3917D58-7183-4B3F-9D22-03F0FE93B956");

    public ElementIdentity()
    : base("Element.Identity", "Element.Identity", "Query element identity information", "Revit", "Element")
    { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Element", "E", "Element to query for its identity", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddParameter(new Parameters.Category(), "Category", "C", "Category in which the Element resides", GH_ParamAccess.item);
      manager.AddParameter(new Parameters.ElementType(), "Type", "T", "ElementType of the Element", GH_ParamAccess.item);
      manager.AddTextParameter("Name", "N", "A human readable name for the Element", GH_ParamAccess.item);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      Autodesk.Revit.DB.Element element = null;
      if (!DA.GetData("Element", ref element))
        return;

      DA.SetData("Category", element?.Category);
      DA.SetData("Type", element?.Document.GetElement(element.GetTypeId()));
      DA.SetData("Name", element?.Name);
    }
  }

  public class ElementMaterials : Component
  {
    public override Guid ComponentGuid => new Guid("93C18DFD-FAAB-4CF1-A681-C11754C2495D");

    public ElementMaterials()
    : base("Element.Materials", "Element.Materials", "Query element used materials", "Revit", "Element")
    { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Element", "E", "Element to query for its materials", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddParameter(new Parameters.Material(), "Materials", "M", "Materials this Element is made of", GH_ParamAccess.list);
      manager.AddParameter(new Parameters.Material(), "Paint", "P", "Materials used to paint this Element", GH_ParamAccess.list);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      DB.Element element = null;
      if (!DA.GetData("Element", ref element))
        return;

      DA.SetDataList("Materials", element?.GetMaterialIds(false).Select(x => element.Document.GetElement(x)));
      DA.SetDataList("Paint",     element?.GetMaterialIds( true).Select(x => element.Document.GetElement(x)));
    }
  }

  public class ElementDelete : TransactionsComponent
  {
    public override Guid ComponentGuid => new Guid("213C1F14-A827-40E2-957E-BA079ECCE700");
    public override GH_Exposure Exposure => GH_Exposure.septenary | GH_Exposure.obscure;
    protected override string IconTag => "X";

    public ElementDelete()
    : base("Element.Delete", "Delete", "Deletes elements from Revit document", "Revit", "Element")
    { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Elements", "E", "Elements to delete", GH_ParamAccess.tree);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager) { }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      if (!DA.GetDataTree<Types.Element>("Elements", out var elementsTree))
        return;

      var elementsToDelete = Parameters.Element.
                             ToElementIds(elementsTree).
                             GroupBy(x => x.Document).
                             ToArray();

      foreach (var group in elementsToDelete)
      {
        BeginTransaction(group.Key);

        try
        {
          var deletedElements = group.Key.Delete(group.Select(x => x.Id).ToArray());

          if (deletedElements.Count == 0)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No elements were deleted");
          else
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{elementsToDelete.Length} elements and {deletedElements.Count - elementsToDelete.Length} dependant elements were deleted.");
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "One or more of the elements cannot be deleted.");
        }
      }
    }
  }

  public class ElementGeometry : ElementGetter
  {
    public override Guid ComponentGuid => new Guid("B7E6A82F-684F-4045-A634-A4AA9F7427A8");
    static readonly string PropertyName = "Geometry";

    public ElementGeometry() : base(PropertyName) { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      base.RegisterInputParams(manager);
      manager[manager.AddParameter(new Parameters.Param_Enum<Types.ViewDetailLevel>(), "DetailLevel", "LOD", ObjectType.Name + " LOD [1, 3]", GH_ParamAccess.item)].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddGeometryParameter(PropertyName, PropertyName.Substring(0, 1), ObjectType.Name + " parameter names", GH_ParamAccess.list);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      DB.Element element = null;
      if (!DA.GetData(ObjectType.Name, ref element))
        return;

      var detailLevel = DB.ViewDetailLevel.Undefined;
      DA.GetData(1, ref detailLevel);
      if (detailLevel == DB.ViewDetailLevel.Undefined)
        detailLevel = DB.ViewDetailLevel.Coarse;

      DB.Options options = null;
      using (var geometry = element?.GetGeometry(detailLevel, out options)) using (options)
      {
        var list = geometry?.ToRhino().Where(x => x is object).ToList();

        DA.SetDataList(PropertyName, list);
      }
    }
  }

  public class ElementPreview : ElementGetter
  {
    public override Guid ComponentGuid => new Guid("A95C7B73-6F70-46CA-85FC-A4402A3B6971");
    static readonly string PropertyName = "Preview";

    public ElementPreview() : base(PropertyName) { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      base.RegisterInputParams(manager);
      manager[manager.AddParameter(new Parameters.Param_Enum<Types.ViewDetailLevel>(), "DetailLevel", "LOD", ObjectType.Name + " LOD [1, 3]", GH_ParamAccess.item)].Optional = true;
      manager[manager.AddNumberParameter("Quality", "Q", ObjectType.Name + " meshes quality [0.0, 1.0]", GH_ParamAccess.item)].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddMeshParameter("Meshes", "M", ObjectType.Name + " meshes", GH_ParamAccess.list);
      manager.AddParameter(new Grasshopper.Kernel.Parameters.Param_OGLShader(), "Materials", "M", ObjectType.Name + " materials", GH_ParamAccess.list);
      manager.AddCurveParameter("Wires", "W", ObjectType.Name + " wires", GH_ParamAccess.list);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      var element = default(DB.Element);
      if (!DA.GetData(ObjectType.Name, ref element))
        return;

      var detailLevel = DB.ViewDetailLevel.Undefined;
      DA.GetData(1, ref detailLevel);
      if (detailLevel == DB.ViewDetailLevel.Undefined)
        detailLevel = DB.ViewDetailLevel.Coarse;

      var relativeTolerance = double.NaN;
      if (DA.GetData(2, ref relativeTolerance))
      {
        if(0.0 > relativeTolerance || relativeTolerance > 1.0)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Parameter '{Params.Input[2].Name}' range is [0.0, 1.0].");
          return;
        }
      }

      var meshingParameters = !double.IsNaN(relativeTolerance) ? new Rhino.Geometry.MeshingParameters(relativeTolerance, Revit.VertexTolerance) : null;
      Types.GeometricElement.BuildPreview(element, meshingParameters, detailLevel, out var materials, out var meshes, out var wires);

      DA.SetDataList(0, meshes?.Select((x) =>    new GH_Mesh(x)));
      DA.SetDataList(1, materials?.Select((x) => new GH_Material(x)));
      DA.SetDataList(2, wires?.Select((x) =>     new GH_Curve(x)));
    }
  }

  public class ElementParameters : ElementGetter
  {
    public override Guid ComponentGuid => new Guid("44515A6B-84EE-4DBD-8241-17EDBE07C5B6");
    static readonly string PropertyName = "Parameters";

    public ElementParameters() : base(PropertyName) { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      base.RegisterInputParams(manager);
      manager[manager.AddTextParameter("Name", "N", "Filter params by Name", GH_ParamAccess.item)].Optional = true;
      manager[manager.AddParameter(new Parameters.Param_Enum<Types.BuiltInParameterGroup>(), "Group", "G", "Filter params by the group they belong", GH_ParamAccess.item)].Optional = true;
      manager[manager.AddBooleanParameter("ReadOnly", "R", "Filter params by its ReadOnly property", GH_ParamAccess.item)].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddParameter(new Parameters.ParameterKey(), "Parameters", "P", "Element parameters", GH_ParamAccess.list);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      DB.Element element = null;
      if (!DA.GetData(ObjectType.Name, ref element))
        return;

      string parameterName = null;
      bool noFilterName = (!DA.GetData("Name", ref parameterName) && Params.Input[1].Sources.Count == 0);

      var builtInParameterGroup = DB.BuiltInParameterGroup.INVALID;
      bool noFilterGroup = (!DA.GetData("Group", ref builtInParameterGroup) && Params.Input[2].Sources.Count == 0);

      bool readOnly = false;
      bool noFilterReadOnly = (!DA.GetData("ReadOnly", ref readOnly) && Params.Input[3].Sources.Count == 0);

      List<DB.Parameter> parameters = null;
      if (element is object)
      {
        parameters = new List<DB.Parameter>(element.Parameters.Size);
        foreach (var group in element.GetParameters(RevitAPI.ParameterSet.Any).GroupBy((x) => x.Definition?.ParameterGroup ?? DB.BuiltInParameterGroup.INVALID).OrderBy((x) => x.Key))
        {
          foreach (var param in group.OrderBy(x => x.Id.IntegerValue))
          {
            if (!noFilterName && parameterName != param.Definition?.Name)
              continue;

            if (!noFilterGroup && builtInParameterGroup != (param.Definition?.ParameterGroup ?? DB.BuiltInParameterGroup.INVALID))
              continue;

            if (!noFilterReadOnly && readOnly != param.IsReadOnly)
              continue;

            parameters.Add(param);
          }
        }
      }

      DA.SetDataList("Parameters", parameters);
    }
  }

  public class ElementDecompose : Component, IGH_VariableParameterComponent
  {
    public override Guid ComponentGuid => new Guid("FAD33C4B-A7C3-479B-B309-8F5363B25599");
    public ElementDecompose() : base("Element.Decompose", "Decompose", "Decomposes an Element into its parameters", "Revit", "Element") { }

    public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
    {
      base.AppendAdditionalMenuItems(menu);

      bool hasInputData = !Params.Input[0].VolatileData.IsEmpty;
      bool hasOutputParameters = Params.Output.Count > 0;

      Menu_AppendSeparator(menu);
      Menu_AppendItem(menu, "Get common parameters", Menu_PopulateOutputsWithCommonParameters, hasInputData, false);
      Menu_AppendItem(menu, "Get all parameters", Menu_PopulateOutputsWithAllParameters, hasInputData, false);
      Menu_AppendItem(menu, "Remove unconnected parameters", Menu_RemoveUnconnectedParameters, hasOutputParameters, false);
    }

    class ComponentAttributes : GH_ComponentAttributes
    {
      public ComponentAttributes(IGH_Component component) : base(component) { }

      public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
      {
        bool ctrl  = Control.ModifierKeys == Keys.Control;
        bool shift = Control.ModifierKeys == Keys.Shift;

        if (e.Button == MouseButtons.Left && (ctrl || shift))
        {
          if (!Owner.Params.Input[0].VolatileData.IsEmpty)
          {
            if (Owner is ElementDecompose elementDecompose)
            {
              sender.ActiveInteraction = null;
              if (shift)
                elementDecompose.Menu_PopulateOutputsWithCommonParameters(sender, e);
              else if (ctrl)
                elementDecompose.Menu_RemoveUnconnectedParameters(sender, e);

              return GH_ObjectResponse.Handled;
            }
          }
        }

        return GH_ObjectResponse.Ignore;
      }
    }

    public override void CreateAttributes() => m_attributes = new ComponentAttributes(this);

    void AddOutputParameter(IGH_Param param)
    {
      if (param.Attributes is null)
        param.Attributes = new GH_LinkedParamAttributes(param, Attributes);

      param.Access = GH_ParamAccess.item;
      Params.RegisterOutputParam(param);
    }

    void Menu_PopulateOutputsWithCommonParameters(object sender, EventArgs e)
    {
      var common = default(HashSet<Parameters.ParameterParam>);
      foreach (var goo in Params.Input[0].VolatileData.AllData(true).OfType<Types.Element>())
      {
        var element = (DB.Element) goo;
        if (element is null)
          continue;

        var current = new HashSet<Parameters.ParameterParam>();
        foreach (var param in element.Parameters.OfType<DB.Parameter>())
          current.Add(new Parameters.ParameterParam(param));

        if (common is null)
          common = current;
        else
          common.IntersectWith(current);
      }

      RecordUndoEvent("Get Common Parameters");

      PopulateOutputParameters(common);
    }

    void Menu_PopulateOutputsWithAllParameters(object sender, EventArgs e)
    {
      var all = new HashSet<Parameters.ParameterParam>();
      foreach (var goo in Params.Input[0].VolatileData.AllData(true).OfType<Types.Element>())
      {
        var element = (DB.Element) goo;
        if (element is null)
          continue;

        foreach (var param in element.Parameters.OfType<DB.Parameter>())
          all.Add(new Parameters.ParameterParam(param));
      }

      RecordUndoEvent("Get All Parameters");

      PopulateOutputParameters(all);
    }

    void PopulateOutputParameters(IEnumerable<Parameters.ParameterParam> parameters)
    {
      var connected = new Dictionary<Parameters.ParameterParam, IList<IGH_Param>>();
      foreach (var output in Params.Output.ToArray())
      {
        if
        (
          output.Recipients.Count > 0 &&
          output is Parameters.ParameterParam param
        )
          connected.Add(param, param.Recipients.ToArray());

        Params.UnregisterOutputParameter(output);
      }

      if (parameters is object)
      {
        foreach (var group in parameters.GroupBy(x => x.ParameterGroup).OrderBy(x => x.Key))
        {
          foreach (var parameter in group.OrderBy(x => x.ParameterBuiltInId))
          {
            AddOutputParameter(parameter);

            if (connected.TryGetValue(parameter, out var recipients))
            {
              foreach (var recipient in recipients)
                recipient.AddSource(parameter);
            }
          }
        }
      }

      Params.OnParametersChanged();
      ExpireSolution(true);
    }

    void Menu_RemoveUnconnectedParameters(object sender, EventArgs e)
    {
      RecordUndoEvent("Remove Unconnected Outputs");

      foreach (var output in Params.Output.ToArray())
      {
        if (output.Recipients.Count > 0)
          continue;

        Params.UnregisterOutputParameter(output);
      }

      Params.OnParametersChanged();
      OnDisplayExpired(false);
    }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Element", "E", "Element to inspect", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager) { }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      DB.Element element = null;
      if (!DA.GetData("Element", ref element))
        return;

      for (int p = 0; p < Params.Output.Count; ++p)
      {
        if (Params.Output[p] is Parameters.ParameterParam param)
          DA.SetData(p, param.GetParameter(element));
      }
    }

    bool IGH_VariableParameterComponent.CanInsertParameter(GH_ParameterSide side, int index) => false;
    bool IGH_VariableParameterComponent.CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output;
    IGH_Param IGH_VariableParameterComponent.CreateParameter(GH_ParameterSide side, int index) => null;
    bool IGH_VariableParameterComponent.DestroyParameter(GH_ParameterSide side, int index) => true;
    void IGH_VariableParameterComponent.VariableParameterMaintenance() { }

    protected override string HtmlHelp_Source()
    {
      var nTopic = new Grasshopper.GUI.HTML.GH_HtmlFormatter(this)
      {
        Title = Name,
        Description =
        @"<p>This component is a special interface object that allows for quick accessing to Revit Element parameters.</p>" +
        @"<p>It's able to modify itself in order to show any parameter its input element parameter contains. " +
        @"It also allows to remove some output parameters if are not connected to anything else.</p>" +
        @"<p>Under the component contextual menu you would find these options:</p>" +
        @"<dl>" +
        @"<dt><b>Get common parameters</b></dt><dd>Populates the output parameters list with common parameters in all input elements</dd>" +
        @"<dt><b>Get all parameters</b></dt><dd>Populates the output parameters list with all parameters found in all input elements</dd>" +
        @"<dt><b>Remove unconnected parameters</b></dt><dd>Removes the output parameters that are not connected to anything else</dd>" +
        @"</dl>",
        ContactURI = @"https://www.rhino3d.com/inside/revit/beta/"
      };

      nTopic.AddRemark("SHIFT + Double click runs \"Get common parameters\"");
      nTopic.AddRemark("CTRL + Double click runs \"Remove unconnected parameters\".");

      return nTopic.HtmlFormat();
    }
  }

  static class ParameterUtils
  {
    public static DB.Parameter GetParameter(IGH_ActiveObject obj, DB.Element element, IGH_Goo key)
    {
      DB.Parameter parameter = null;
      switch (key as Types.ParameterKey ?? key.ScriptVariable())
      {
        case Types.ParameterKey parameterKey:
          if (parameterKey.Document.Equals(element.Document))
          {
            if (Enum.IsDefined(typeof(DB.BuiltInParameter), parameterKey.Id.IntegerValue))
            {
              parameter = element.get_Parameter((DB.BuiltInParameter) parameterKey.Id.IntegerValue);
              if (parameter is null)
                obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{DB.LabelUtils.GetLabelFor((DB.BuiltInParameter) parameterKey.Id.IntegerValue)}' not defined in 'Element'");
            }
            else if (element.Document.GetElement(parameterKey.Id) is DB.ParameterElement parameterElement)
            {
              parameter = element.get_Parameter(parameterElement.GetDefinition());
              if (parameter is null)
                obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{parameterElement.Name}' not defined in 'Element'");
            }
            else
              obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Data conversion failed from {key.TypeName} to Revit Parameter element");
          }
          else
            obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"'ParameterKey' doesn't belong same document as 'Element'");

          break;

        case DB.Parameter param:
          if (param.Element.Document.Equals(element.Document) && param.Element.Id == element.Id)
            parameter = param;
          else
            obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Parameter '{param.Definition.Name}' doesn't belong to 'Element'");

          break;

        case string parameterName:
          parameter = element.GetParameter(parameterName, RevitAPI.ParameterSet.Any);
          if (parameter is null)
            obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{parameterName}' not defined in 'Element'");
          break;

        case int parameterId:
          if (Enum.IsDefined(typeof(DB.BuiltInParameter), parameterId))
          {
            parameter = element.get_Parameter((DB.BuiltInParameter) parameterId);
            if (parameter is null)
              obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{DB.LabelUtils.GetLabelFor((DB.BuiltInParameter) parameterId)}' not defined in 'Element'");
          }
          else if (element.Document.GetElement(new DB.ElementId(parameterId)) is DB.ParameterElement parameterElement)
          {
            parameter = element.get_Parameter(parameterElement.GetDefinition());
            if (parameter is null)
              obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{parameterElement.Name}' not defined in 'Element'");
          }
          else
            obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Data conversion failed from {key.TypeName} to Revit Parameter element");
          break;

        case DB.ElementId parameterElementId:
          if (Enum.IsDefined(typeof(DB.BuiltInParameter), parameterElementId.IntegerValue))
          {
            parameter = element.get_Parameter((DB.BuiltInParameter) parameterElementId.IntegerValue);
            if (parameter is null)
              obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{DB.LabelUtils.GetLabelFor((DB.BuiltInParameter) parameterElementId.IntegerValue)}' not defined in 'Element'");
          }
          else if (element.Document.GetElement(parameterElementId) is DB.ParameterElement parameterElement)
          {
            parameter = element.get_Parameter(parameterElement.GetDefinition());
            if (parameter is null)
              obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{parameterElement.Name}' not defined in 'Element'");
          }
          else
            obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Data conversion failed from {key.TypeName} to Revit Parameter element");
          break;

        case Guid guid:
          parameter = element.get_Parameter(guid);
          if (parameter is null)
            obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Parameter '{guid}' not defined in 'Element'");
          break;

        default:
          obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Data conversion failed from {key.TypeName} to Revit Parameter element");
          break;
      }

      return parameter;
    }

    public static bool SetParameter(IGH_ActiveObject obj, DB.Element element, DB.Parameter parameter, IGH_Goo goo)
    {
      try
      {
        var value = goo.ScriptVariable();
        switch (parameter?.StorageType)
        {
          case DB.StorageType.Integer:
          {
            switch (value)
            {
              case bool boolean: parameter.Set(boolean ? 1 : 0); break;
              case int integer: parameter.Set(integer); break;
              case double real: parameter.Set(Math.Round(ToHost(real, parameter.Definition.ParameterType)).Clamp(int.MinValue, int.MaxValue)); break;
              case System.Drawing.Color color: parameter.Set(((int) color.R) | ((int) color.G << 8) | ((int) color.B << 16)); break;
              default: element = null; break;
            }
            break;
          }
          case DB.StorageType.Double:
          {
            switch (value)
            {
              case int integer: parameter.Set((double) integer); break;
              case double real: parameter.Set(ToHost(real, parameter.Definition.ParameterType)); break;
              default: element = null; break;
            }
            break;
          }
          case DB.StorageType.String:
          {
            switch (value)
            {
              case string str: parameter.Set(str); break;
              default: element = null; break;
            }
            break;
          }
          case DB.StorageType.ElementId:
          {
            switch (value)
            {
              case DB.Element ele: parameter.Set(ele.Id); break;
              case DB.Category cat: parameter.Set(cat.Id); break;
              case int integer: parameter.Set(new DB.ElementId(integer)); break;
              default: element = null; break;
            }
            break;
          }
          default:
          {
            element = null;
            break;
          }
        }

        if (element is null && parameter is object)
          obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format("Unable to cast 'Value' from {0} to {1}.", value.GetType().Name, parameter.StorageType.ToString()));

        return true;
      }
      catch (Exception e)
      {
        obj.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, string.Format("Unable to set 'Value' to parameter {0} : {1}", parameter.Definition.Name, e.Message));
      }

      return false;
    }

    static double ToHost(double value, DB.ParameterType type)
    {
      switch (type)
      {
        case DB.ParameterType.Length: return value / Math.Pow(Revit.ModelUnits, 1.0);
        case DB.ParameterType.Area:   return value / Math.Pow(Revit.ModelUnits, 2.0);
        case DB.ParameterType.Volume: return value / Math.Pow(Revit.ModelUnits, 3.0);
      }

      return value;
    }
  }

  public class ElementParameterGet : Component
  {
    public override Guid ComponentGuid => new Guid("D86050F2-C774-49B1-9973-FB3AB188DC94");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public ElementParameterGet()
    : base("Element.ParameterGet", "ParameterGet", "Gets the parameter value of a specified Revit Element", "Revit", "Element")
    {}

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Element", "E", "Element to query", GH_ParamAccess.item);
      manager.AddGenericParameter("ParameterKey", "K", "Element parameter to query", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddParameter(new Parameters.ParameterValue(), "ParameterValue", "V", "Element parameter value", GH_ParamAccess.item);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      DB.Element element = null;
      if (!DA.GetData("Element", ref element))
        return;

      IGH_Goo parameterKey = null;
      if (!DA.GetData("ParameterKey", ref parameterKey))
        return;

      var parameter = ParameterUtils.GetParameter(this, element, parameterKey);
      DA.SetData("ParameterValue", parameter);
    }
  }

  public class ElementParameterSet : TransactionsComponent
  {
    public override Guid ComponentGuid => new Guid("8F1EE110-7FDA-49E0-BED4-E8E0227BC021");
    public override GH_Exposure Exposure => GH_Exposure.primary;

    public ElementParameterSet()
    : base("Element.ParameterSet", "ParameterSet", "Sets the parameter value of a specified Revit Element", "Revit", "Element")
    { }

    protected override void RegisterInputParams(GH_InputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Element", "E", "Element to update", GH_ParamAccess.item);
      manager.AddGenericParameter("ParameterKey", "K", "Element parameter to modify", GH_ParamAccess.item);
      manager.AddGenericParameter("ParameterValue", "V", "Element parameter value", GH_ParamAccess.item);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager manager)
    {
      manager.AddParameter(new Parameters.Element(), "Element", "E", "Updated Element", GH_ParamAccess.item);
    }

    protected override void TrySolveInstance(IGH_DataAccess DA)
    {
      DB.Element element = null;
      if (!DA.GetData("Element", ref element))
        return;

      IGH_Goo key = null;
      if (!DA.GetData("ParameterKey", ref key))
        return;

      IGH_Goo value = null;
      if (!DA.GetData("ParameterValue", ref value))
        return;

      var parameter = ParameterUtils.GetParameter(this, element, key);
      if (parameter is null)
        return;

      BeginTransaction(element.Document);

      if (ParameterUtils.SetParameter(this, element, parameter, value))
        DA.SetData("Element", element);
    }
  }
}
