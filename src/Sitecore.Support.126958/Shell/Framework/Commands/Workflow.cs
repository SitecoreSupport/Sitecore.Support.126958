namespace Sitecore.Support.Shell.Framework.Commands
{
  using System;
  using System.Collections.Specialized;
  using Sitecore.Configuration;
  using Sitecore.Data;
  using System.Linq;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Globalization;
  using Sitecore.Pipelines;
  using Sitecore.Web.UI.Sheer;
  using Sitecore.Workflows;
  using Sitecore.Workflows.Simple;
  using Version = Sitecore.Data.Version;
  using System.Collections.Generic;
  using Sitecore.Shell.Framework.Commands;

  /// <summary>
  /// Represents the Workflow command.
  /// </summary>
  [Serializable]
  public class Workflow : Command
  {

    /// <summary>Key used to identify the ID</summary>
    protected const string IDKey = "id";

    /// <summary>Key used to identify the language</summary>
    protected const string LanguageKey = "language";

    /// <summary>Key used to identify the version</summary>
    protected const string VersionKey = "version";

    /// <summary>Key used to identify the command ID</summary>
    protected const string CommandIdKey = "commandid";

    /// <summary>Key used to identify the workflow ID</summary>
    protected const string WorkflowIdKey = "workflowid";

    /// <summary>Key used to identify the UI setting</summary>
    protected const string UIKey = "ui";

    /// <summary>Key used to identify the 'check modified' setting</summary>
    protected const string CheckModifiedKey = "checkmodified";

    /// <summary>Key used to identify the 'suppress comment' setting</summary>
    protected const string SuppressCommentKey = "suppresscomment";

    /// <summary>
    /// Queries the state of the command.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>
    /// The state of the command.
    /// </returns>
    public override CommandState QueryState(CommandContext context)
    {
      if (!Settings.Workflows.Enabled)
      {
        return CommandState.Hidden;
      }

      return base.QueryState(context);
    }

    /// <summary>
    /// Executes the command in the specified context.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Execute(CommandContext context)
    {
      Assert.ArgumentNotNull(context, "context");

      string id = context.Parameters["id"];
      string language = context.Parameters["language"];
      string version = context.Parameters["version"];

      var item = Client.ContentDatabase.Items[id, Language.Parse(language), Version.Parse(version)];

      if (item == null)
      {
        return;
      }

      // check if workflow state was changed after the item was loaded in CE
      // to support concurrent execution.
      if (!CheckCommandValidity(item, context.Parameters["commandid"]))
        return;

      var parameters = new NameValueCollection {
        {IDKey, id},
        {LanguageKey, language},
        {VersionKey, version},
        {CommandIdKey, context.Parameters["commandid"]},
        {WorkflowIdKey, context.Parameters["workflowid"]},
        {UIKey, context.Parameters["ui"]},
        {CheckModifiedKey, "1"},
        {SuppressCommentKey, context.Parameters["suppresscomment"]}
      };

      Context.ClientPage.Start(this, "Run", parameters);
    }

    /// <summary>
    /// Runs the specified args.
    /// </summary>
    /// <param name="args">The arguments.</param>
    protected void Run(ClientPipelineArgs args)
    {
      Assert.ArgumentNotNull(args, "args");

      bool isPostBack = args.IsPostBack;
      bool checkModified = args.Parameters[CheckModifiedKey] == "1";
      var item = Client.ContentDatabase.Items[args.Parameters["id"], Language.Parse(args.Parameters["language"]), Version.Parse(args.Parameters["version"])];

      if (!CheckCommandValidity(item, args.Parameters["commandid"]))
      {
        return;
      }

      // checkModified
      if (checkModified)
      {
        if (!isPostBack)
        {
          if (Context.ClientPage.Modified)
          {
            CheckModifiedParameters checkModifiedParameters = new CheckModifiedParameters()
            {
              ResumePreviousPipeline = true
            };
            SheerResponse.CheckModified(checkModifiedParameters);

            args.WaitForPostBack();

            return;
          }
        }
        else
        {
          if (args.Result == "cancel")
          {
            return;
          }
        }

        args.Parameters[CheckModifiedKey] = null;
        isPostBack = false;
      }

      if (isPostBack && args.Result == "cancel")
        return;

      var commentFields = new Sitecore.Collections.StringDictionary();

      bool ui = StringUtil.GetString(args.Parameters[UIKey]) != "1";
      bool suppressComment = StringUtil.GetString(args.Parameters[SuppressCommentKey]) == "1";
      var commandIDText = args.Parameters[CommandIdKey];
      var workflowIDText = args.Parameters[WorkflowIdKey];

      var itemUri = new ItemUri(args.Parameters[IDKey], Language.Parse(args.Parameters[LanguageKey]), Version.Parse(args.Parameters[VersionKey]), Client.ContentDatabase);

      if (!isPostBack && ui && !suppressComment)
      {
        var commandId = ID.Null;
        ID.TryParse(commandIDText, out commandId);

        WorkflowUIHelper.DisplayCommentDialog(new List<ItemUri> { itemUri }, commandId);

        args.WaitForPostBack();

        return;
      }

      if (isPostBack)
      {
        if (args.Result == "null" || args.Result == "undefined")
        {
          return;
        }

        var comment = args.Result;
        commentFields = WorkflowUIHelper.ExtractFieldsFromFieldEditor(comment);
      }

      var callbackProcessor = new Processor("Workflow completed callback", this, "WorkflowCompleteCallback");
      WorkflowUIHelper.ExecuteCommand(itemUri, workflowIDText, commandIDText, commentFields, callbackProcessor);
    }

    /// <summary>
    /// Processor delegate to be executed when workflow completes successfully.
    /// </summary>
    /// <param name="args">The arguments for the workflow invocation.</param>
    [UsedImplicitly]
    protected void WorkflowCompleteCallback(WorkflowPipelineArgs args)
    {
      if (args.CommentFields.Count > 0)
      {
        Context.ClientPage.SendMessage(this, "item:refresh");
      }
      else
      {
        SheerResponse.Redraw();
      }
    }

    #region Private methods

    /// <summary>
    /// Checks if this command can be executed against current workflow state. This is mainly about concurrent workflow transitions.
    /// </summary>
    /// <param name="item">the item</param>
    /// <param name="commandId">workflow command</param>
    /// <returns></returns>
    private bool CheckCommandValidity(Item item, string commandId)
    {
      Assert.ArgumentNotNullOrEmpty(commandId, "commandId");
      Assert.ArgumentNotNull(item, "item");

      var workflow = item.State.GetWorkflow();
      var state = item.State.GetWorkflowState();

      Assert.IsNotNull(workflow, "workflow");
      Assert.IsNotNull(state, "state");

      if (!workflow.GetCommands(state.StateID).Any(a => a.CommandID == commandId))
      {
        SheerResponse.Alert(Texts.TheItemHasBeenMovedToADifferentWorkflowStateSitecoreWillThereforReloadTheItem);
        Context.ClientPage.SendMessage(this, "item:refresh");
        return false;
      }
      return true;
    }

    #endregion
  }
}
