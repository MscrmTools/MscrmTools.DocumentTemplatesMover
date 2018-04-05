using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;
using XrmToolBox.Extensibility.Interfaces;

namespace MsCrmTools.DocumentTemplatesMover
{
    public partial class PluginControl : MultipleConnectionsPluginControlBase, IGitHubPlugin, IHelpPlugin, IStatusBarMessenger
    {
        #region Variables

        private int currentsColumnOrder;
        private readonly TemplatesManager tManager;

        #endregion Variables

        #region Constructor

        public PluginControl()
        {
            InitializeComponent();
            tManager = new TemplatesManager();
        }

        #endregion Constructor

        #region XrmToolbox

        public event EventHandler<StatusBarMessageEventArgs> SendMessageToStatusBar;

        public string HelpUrl => "https://github.com/MscrmTools/MscrmTools.DocumentTemplatesMover/wiki";

        public string RepositoryName => "MsCrmTools.DocumentTemplatesMover";

        public string UserName => "MscrmTools";

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName = "", object parameter = null)
        {
            ConnectionDetail = detail;
            if (actionName == "AdditionalOrganization")
            {
                AdditionalConnectionDetails.Clear();
                AdditionalConnectionDetails.Add(detail);
                SetConnectionLabel(detail, "Target");
            }
            else
            {
                SetConnectionLabel(detail, "Source");
            }

            base.UpdateConnection(newService, detail, actionName, parameter);
        }

        protected override void ConnectionDetailsUpdated(NotifyCollectionChangedEventArgs e)
        {
        }

        #endregion XrmToolbox

        #region UI Events

        private void BtnSelectTargetClick(object sender, EventArgs e)
        {
            AddAdditionalOrganization();
        }

        private void lvTemplates_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (e.Column == currentsColumnOrder)
            {
                lvTemplates.Sorting = lvTemplates.Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;

                lvTemplates.ListViewItemSorter = new ListViewItemComparer(e.Column, lvTemplates.Sorting);
            }
            else
            {
                currentsColumnOrder = e.Column;
                lvTemplates.ListViewItemSorter = new ListViewItemComparer(e.Column, SortOrder.Ascending);
            }
        }

        private void TsbLoadTemplatesClick(object sender, EventArgs e)
        {
            ExecuteMethod(RetrieveTemplates);
        }

        private void TsbTransfertTemplatesClick(object sender, EventArgs e)
        {
            if (lvTemplates.SelectedItems.Count > 0 && AdditionalConnectionDetails.Count > 0)
            {
                var selectedTemplates = lvTemplates.SelectedItems.Cast<ListViewItem>().Select(item => (Entity)item.Tag).ToList();

                lbLogs.Items.Clear();
                tsbLoadTemplates.Enabled = false;
                tsbTransfertTemplates.Enabled = false;
                btnSelectTarget.Enabled = false;
                Cursor = Cursors.WaitCursor;

                var worker = new BackgroundWorker();
                worker.DoWork += (s, evt) =>
                {
                    List<Entity> templates = (List<Entity>)evt.Argument;
                    var total = templates.Count;
                    var current = 0;
                    var targetService = AdditionalConnectionDetails.First().GetCrmServiceClient();

                    foreach (var template in templates)
                    {
                        current++;

                        string etc = template.GetAttributeValue<string>("associatedentitytypecode");
                        string name = template.GetAttributeValue<string>("name");

                        SendMessageToStatusBar(this, new StatusBarMessageEventArgs(current * 100 / total, "Processing template '" + name + "'..."));

                        try
                        {
                            int? oldEtc = tManager.GetEntityTypeCode(Service, etc);
                            int? newEtc = tManager.GetEntityTypeCode(targetService, etc);

                            tManager.ReRouteEtcViaOpenXML(template, name, etc, oldEtc, newEtc);

                            var templateToTransfer = new Entity(template.LogicalName);
                            foreach (var attribute in template.Attributes)
                            {
                                templateToTransfer[attribute.Key] = attribute.Value;
                            }
                            templateToTransfer["associatedentitytypecode"] = newEtc;

                            Guid existingId = tManager.TemplateExists(targetService, name);
                            if (existingId != null && existingId != Guid.Empty)
                            {
                                templateToTransfer["documenttemplateid"] = existingId;

                                targetService.Update(templateToTransfer);
                            }
                            else
                            {
                                targetService.Create(templateToTransfer);
                            }

                            Log(name, true);
                        }
                        catch (Exception error)
                        {
                            Log(name, false, error.Message);
                        }
                    }
                };
                worker.ProgressChanged += (s, evt) =>
                {
                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(0, evt.UserState.ToString()));
                };
                worker.RunWorkerCompleted += (s, evt) =>
                {
                    if (evt.Error != null)
                    {
                        MessageBox.Show(ParentForm, @"An error has occured while transferring templates: " + evt.Error.Message, @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    tsbLoadTemplates.Enabled = true;
                    tsbTransfertTemplates.Enabled = true;
                    btnSelectTarget.Enabled = true;
                    Cursor = Cursors.Default;

                    SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(string.Empty));
                };
                worker.WorkerReportsProgress = true;
                worker.RunWorkerAsync(selectedTemplates);
            }
            else
            {
                MessageBox.Show(@"You have to select at least one source template and a target organization to continue.", @"Warning",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion UI Events

        #region Methods

        private void Log(string name, bool succeeded, string message = null)
        {
            if (InvokeRequired)
            {
                Invoke((MethodInvoker)delegate
                {
                    lbLogs.Items.Add(
                        $"{(succeeded ? "Success" : "Error")}: {name}{(message != null ? " : " + message : "")}");
                });
            }
        }

        /// <summary>
        /// Retrieves templates from the source organization
        /// </summary>
        private void RetrieveTemplates()
        {
            lvTemplates.Items.Clear();

            tsbLoadTemplates.Enabled = false;
            tsbTransfertTemplates.Enabled = false;
            btnSelectTarget.Enabled = false;
            Cursor = Cursors.WaitCursor;

            SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs("Loading templates..."));

            var bw = new BackgroundWorker();
            bw.DoWork += (sender, e) =>
            {
                e.Result = tManager.GetTemplates(Service);
            };
            bw.RunWorkerCompleted += (sender, e) =>
            {
                if (e.Error == null)
                {
                    foreach (var template in (List<Entity>)e.Result)
                    {
                        var item = new ListViewItem();
                        item.Tag = template;
                        item.Text = template.GetAttributeValue<string>("name");
                        lvTemplates.Items.Add(item);
                    }
                }
                else
                {
                    MessageBox.Show(ParentForm, @"An error has occured while loading templates: " + e.Error.Message, @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                tsbLoadTemplates.Enabled = true;
                tsbTransfertTemplates.Enabled = true;
                btnSelectTarget.Enabled = true;
                Cursor = Cursors.Default;

                SendMessageToStatusBar?.Invoke(this, new StatusBarMessageEventArgs(string.Empty));
            };
            bw.RunWorkerAsync();
        }

        /// <summary>
        /// Sets the connections labels on either the source/target section
        /// </summary>
        /// <param name="detail"></param>
        /// <param name="serviceType"></param>
        private void SetConnectionLabel(ConnectionDetail detail, string serviceType)
        {
            switch (serviceType)
            {
                case "Source":
                    lblSource.Text = detail.ConnectionName;
                    lblSource.ForeColor = Color.Green;
                    break;

                case "Target":
                    lblTarget.Text = detail.ConnectionName;
                    lblTarget.ForeColor = Color.Green;
                    break;
            }
        }

        #endregion Methods
    }
}