using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Args;
using XrmToolBox.Extensibility.Interfaces;

namespace MsCrmTools.DocumentTemplatesMover
{
    public partial class PluginControl : UserControl, IXrmToolBoxPluginControl, IGitHubPlugin, IHelpPlugin, IStatusBarMessenger
    {
        #region Variables

        private int currentsColumnOrder;
        private ConnectionDetail detail;
        private IOrganizationService service;
        private IOrganizationService targetService;
        private TemplatesManager tManager;

        #endregion Variables

        #region Constructor

        public PluginControl()
        {
            InitializeComponent();
            tManager = new TemplatesManager();
        }

        #endregion Constructor

        #region XrmToolbox

        public event EventHandler OnCloseTool;

        public event EventHandler OnRequestConnection;

        public event EventHandler<StatusBarMessageEventArgs> SendMessageToStatusBar;

        public string HelpUrl
        {
            get
            {
                return "https://github.com/MscrmTools/DamSim.SolutionTransferTool/wiki";
            }
        }

        public Image PluginLogo
        {
            get { return imageList1.Images[0]; }
        }

        public string RepositoryName
        {
            get
            {
                return "MsCrmTools.DocumentTemplatesMover";
            }
        }

        public IOrganizationService Service
        {
            get { return service; }
        }

        public string UserName
        {
            get
            {
                return "MscrmTools";
            }
        }

        public string GetCompany()
        {
            return GetType().GetCompany();
        }

        public string GetMyType()
        {
            return GetType().FullName;
        }

        public string GetVersion()
        {
            return GetType().Assembly.GetName().Version.ToString();
        }

        public void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName = "", object parameter = null)
        {
            this.detail = detail;
            if (actionName == "TargetOrganization")
            {
                targetService = newService;
                SetConnectionLabel(detail, "Target");
            }
            else
            {
                service = newService;
                SetConnectionLabel(detail, "Source");
            }
        }

        #endregion XrmToolbox

        #region UI Events

        private void BtnCloseClick(object sender, EventArgs e)
        {
            OnCloseTool(this, null);
        }

        private void BtnSelectTargetClick(object sender, EventArgs e)
        {
            if (OnRequestConnection != null)
            {
                var args = new RequestConnectionEventArgs { ActionName = "TargetOrganization", Control = this };
                OnRequestConnection(this, args);
            }
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
            if (service == null)
            {
                if (OnRequestConnection != null)
                {
                    var args = new RequestConnectionEventArgs { Control = this };
                    OnRequestConnection(this, args);
                }
            }
            else
            {
                RetrieveTemplates();
            }
        }

        private void TsbTransfertTemplatesClick(object sender, EventArgs e)
        {
            if (lvTemplates.SelectedItems.Count > 0 && targetService != null)
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

                    foreach (var template in templates)
                    {
                        current++;

                        string etc = template.GetAttributeValue<string>("associatedentitytypecode");
                        string name = template.GetAttributeValue<string>("name");

                        SendMessageToStatusBar(this, new StatusBarMessageEventArgs(current * 100 / total, "Processing template '" + name + "'..."));

                        try
                        {
                            int? oldEtc = tManager.GetEntityTypeCode(service, etc);
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
                                Guid id = targetService.Create(templateToTransfer);
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
                    SendMessageToStatusBar(this, new StatusBarMessageEventArgs(0, evt.UserState.ToString()));
                };
                worker.RunWorkerCompleted += (s, evt) =>
                {
                    if (evt.Error != null)
                    {
                        MessageBox.Show(ParentForm, "An error has occured while transferring templates: " + evt.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    tsbLoadTemplates.Enabled = true;
                    tsbTransfertTemplates.Enabled = true;
                    btnSelectTarget.Enabled = true;
                    Cursor = Cursors.Default;

                    SendMessageToStatusBar(this, new StatusBarMessageEventArgs(string.Empty));
                };
                worker.WorkerReportsProgress = true;
                worker.RunWorkerAsync(selectedTemplates);
            }
            else
            {
                MessageBox.Show("You have to select at least one source template and a target organization to continue.", "Warning",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion UI Events

        #region Methods

        private void Log(string name, bool succeeded, string message = null)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    lbLogs.Items.Add(string.Format("{0}: {1}{2}",
                        succeeded ? "Success" : "Error",
                        name,
                        message != null ? " : " + message : ""
                        ));
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

            SendMessageToStatusBar(this, new StatusBarMessageEventArgs("Loading templates..."));

            var bw = new BackgroundWorker();
            bw.DoWork += (sender, e) =>
            {
                e.Result = tManager.GetTemplates(service);
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
                    MessageBox.Show(ParentForm, "An error has occured while loading templates: " + e.Error.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                tsbLoadTemplates.Enabled = true;
                tsbTransfertTemplates.Enabled = true;
                btnSelectTarget.Enabled = true;
                Cursor = Cursors.Default;

                SendMessageToStatusBar(this, new StatusBarMessageEventArgs(string.Empty));
            };
            bw.RunWorkerAsync();
        }

        /// <summary>
        /// Sets the connections labels on either the source/target section
        /// </summary>
        /// <param name="serviceToLabel"></param>
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

        public void ClosingPlugin(PluginCloseInfo info)
        {
            if (info.FormReason != CloseReason.None ||
                info.ToolBoxReason == ToolBoxCloseReason.CloseAll ||
                info.ToolBoxReason == ToolBoxCloseReason.CloseAllExceptActive)
            {
                return;
            }

            info.Cancel = MessageBox.Show(@"Are you sure you want to close this tab?", @"Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes;
        }
    }
}