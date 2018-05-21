namespace Sitecore.Shell.Framework.Commands
{
  using Sitecore;
  using Sitecore.Collections;
  using Sitecore.Configuration;
  using Sitecore.Data;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Globalization;
  using Sitecore.Pipelines;
  using Sitecore.Web.UI.Sheer;
  using Sitecore.Workflows;
  using Sitecore.Workflows.Simple;
  using global::System;
  using global::System.Collections.Specialized;
  using global::System.Linq;

  [Serializable]
  public class Workflow : Command
  {
    protected const string CheckModifiedKey = "checkmodified";
    protected const string CommandIdKey = "commandid";
    protected const string IDKey = "id";
    protected const string LanguageKey = "language";
    protected const string SuppressCommentKey = "suppresscomment";
    protected const string UIKey = "ui";
    protected const string VersionKey = "version";
    protected const string WorkflowIdKey = "workflowid";

    private bool CheckCommandValidity(Item item, string commandId)
    {
      Assert.ArgumentNotNullOrEmpty(commandId, "commandId");
      Assert.ArgumentNotNull(item, "item");
      IWorkflow workflow = item.State.GetWorkflow();
      WorkflowState workflowState = item.State.GetWorkflowState();
      Assert.IsNotNull(workflow, "workflow");
      Assert.IsNotNull(workflowState, "state");
      if (!workflow.GetCommands(workflowState.StateID).Any<WorkflowCommand>(a => (a.CommandID == commandId)))
      {
        SheerResponse.Alert("The item has been moved to a different workflow state. Sitecore will therefore reload the item.", new string[0]);
        Context.ClientPage.SendMessage(this, "item:refresh");
        return false;
      }
      return true;
    }

    public override void Execute(CommandContext context)
    {
      Assert.ArgumentNotNull(context, "context");
      string str = context.Parameters["id"];
      string name = context.Parameters["language"];
      string str3 = context.Parameters["version"];
      Item item = Sitecore.Client.ContentDatabase.Items[str, Language.Parse(name), Sitecore.Data.Version.Parse(str3)];
      if ((item != null) && this.CheckCommandValidity(item, context.Parameters["commandid"]))
      {
        NameValueCollection values2 = new NameValueCollection();
        values2.Add("id", str);
        values2.Add("language", name);
        values2.Add("version", str3);
        values2.Add("commandid", context.Parameters["commandid"]);
        values2.Add("workflowid", context.Parameters["workflowid"]);
        values2.Add("ui", context.Parameters["ui"]);
        values2.Add("checkmodified", "1");
        values2.Add("suppresscomment", context.Parameters["suppresscomment"]);
        NameValueCollection parameters = values2;
        Context.ClientPage.Start(this, "Run", parameters);
      }
    }

    public override CommandState QueryState(CommandContext context)
    {
      if (!Settings.Workflows.Enabled)
      {
        return CommandState.Hidden;
      }
      return base.QueryState(context);
    }

    protected void Run(ClientPipelineArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      bool isPostBack = args.IsPostBack;
      bool flag2 = args.Parameters["checkmodified"] == "1";
      Item item = Sitecore.Client.ContentDatabase.Items[args.Parameters["id"], Language.Parse(args.Parameters["language"]), Sitecore.Data.Version.Parse(args.Parameters["version"])];
      if (this.CheckCommandValidity(item, args.Parameters["commandid"]))
      {
        if (flag2)
        {
          if (!isPostBack)
          {
            if (Context.ClientPage.Modified)
            {
              SheerResponse.CheckModified(true);
              args.WaitForPostBack();
              return;
            }
          }
          else if (args.Result == "cancel")
          {
            return;
          }
          args.Parameters["checkmodified"] = null;
          isPostBack = false;
        }
        if (!isPostBack || (args.Result != "cancel"))
        {
          Sitecore.Collections.StringDictionary commentFields = new Sitecore.Collections.StringDictionary();
          bool flag3 = StringUtil.GetString(new string[] { args.Parameters["ui"] }) != "1";
          bool flag4 = StringUtil.GetString(new string[] { args.Parameters["suppresscomment"] }) == "1";
          string str = args.Parameters["commandid"];
          string workflowId = args.Parameters["workflowid"];
          ItemUri itemUri = new ItemUri(args.Parameters["id"], Language.Parse(args.Parameters["language"]), Sitecore.Data.Version.Parse(args.Parameters["version"]), Sitecore.Client.ContentDatabase);
          if ((!isPostBack && flag3) && !flag4)
          {
            ID @null = ID.Null;
            ID.TryParse(str, out @null);
            WorkflowUIHelper.DisplayCommentDialog(itemUri, @null);
            args.WaitForPostBack();
          }
          else
          {
            if (isPostBack)
            {
              if ((args.Result == "null") || (args.Result == "undefined"))
              {
                return;
              }
              commentFields = WorkflowUIHelper.ExtractFieldsFromFieldEditor(args.Result);
            }
            Processor completionCallback = new Processor("Workflow completed callback", this, "WorkflowCompleteCallback");
            WorkflowUIHelper.ExecuteCommand(itemUri, workflowId, str, commentFields, completionCallback);
          }
        }
      }
    }

    [UsedImplicitly]
    protected void WorkflowCompleteCallback(WorkflowPipelineArgs args)
    {
      Context.ClientPage.SendMessage(this, "item:refresh");
    }
  }
}
