using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;


namespace AliantDesktop
{
    public partial class IdentificationForm : Form
    {
        public AliantWebContext dbContext { get; set; }

        List<Element> sensors;
        List<ChannelDataTypeElementType> channelDataTypeElementTypeList;
        public Guid StructureId { get; set; }
        IEnumerable<Guid?> elementTypesIDs;
        List<ChannelDataType> channelDataTypeList;
        List<ChannelDataType> selectedChannelDataTypes { get; set; } = new List<ChannelDataType>();
        List<Element> selectedElements { get; set; } = new List<Element>();



        private async void IdentificationForm_Load(object sender, EventArgs e)
        {


            sensors = await dbContext.Elements.Include(e => e.ElementType)
.ThenInclude(et => et.ChannelDataTypeElementTypes)
// Apply the filtering conditions
.Where(a => a.StructureId == StructureId
            && (a.ElementType != null && a.ElementType.ChannelDataTypeElementTypes.Count > 0))
.ToListAsync();




            elementTypesIDs = sensors.Select(x => x.ElementTypeId);


            channelDataTypeElementTypeList = await dbContext.ChannelDataTypeElementTypes.Include(e => e.ChannelDataType)

.Where(a => elementTypesIDs.Contains(a.ElementTypeId))
.ToListAsync();


            channelDataTypeList = channelDataTypeElementTypeList.Select(x => x.ChannelDataType).ToList();

            if (channelDataTypeList.Count() > 0)
            {

            }
            else
            {
                MessageBox.Show("No related Data in the database...", "Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            dataGridViewSensors.AutoGenerateColumns = false;
            dataGridViewSensors.DataSource = sensors;
            dataGridViewSensors.CellValueChanged += DataGridViewSensors_CellValueChanged;
            dataGridViewSensors.CurrentCellDirtyStateChanged += DataGridViewSensors_CurrentCellDirtyStateChanged;

            ChannelDataTypeDataGridView.AutoGenerateColumns = false;
            ChannelDataTypeDataGridView.DataSource = channelDataTypeList;
            ChannelDataTypeDataGridView.CellValueChanged += ChannelDataTypeDataGridView_CellValueChanged;
            ChannelDataTypeDataGridView.CurrentCellDirtyStateChanged += ChannelDataTypeDataGridView_CurrentCellDirtyStateChanged;




        }
        private void DataGridViewSensors_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == dataGridViewSensors.Columns["ActionColumn"].Index && e.RowIndex >= 0)
            {
                var isChecked = Convert.ToBoolean(dataGridViewSensors.Rows[e.RowIndex].Cells["ActionColumn"].Value);
                var element = dataGridViewSensors.Rows[e.RowIndex].DataBoundItem as Element;
                OnChangeSelectedElements(isChecked, element);
            }
        }
        private void DataGridViewSensors_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dataGridViewSensors.IsCurrentCellDirty)
            {
                dataGridViewSensors.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void ChannelDataTypeDataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == ChannelDataTypeDataGridView.Columns["CDTActionColumn"].Index && e.RowIndex >= 0)
            {
                var isChecked = Convert.ToBoolean(ChannelDataTypeDataGridView.Rows[e.RowIndex].Cells["CDTActionColumn"].Value);
                var element = ChannelDataTypeDataGridView.Rows[e.RowIndex].DataBoundItem as ChannelDataType;
                OnChangeSelectedChannelDataTypes(isChecked, element);
            }
        }
        private void ChannelDataTypeDataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (ChannelDataTypeDataGridView.IsCurrentCellDirty)
            {
                ChannelDataTypeDataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        void OnChangeSelectedElements(bool args, Element element)
        {
            if (args)
            {

                selectedElements.Add(element);
            }
            else
            {
                selectedElements.Remove(element);
            }
        }


        void OnChangeSelectedChannelDataTypes(bool args, ChannelDataType channelDataType)
        {
            if (args)
            {

                selectedChannelDataTypes.Add(channelDataType);
            }
            else
            {
                selectedChannelDataTypes.Remove(channelDataType);
            }
        }




        public IdentificationForm()
        {



            InitializeComponent();



            this.Load += IdentificationForm_Load;


        }

        private void Ibrahimbutton_Click(object sender, EventArgs e)
        {
       
            var listIDs = selectedElements.Select(x => x.ElementId);
    

            var listCDTIDs = selectedChannelDataTypes.Select(x => x.ChannelDataTypeId);
          


            if (listIDs.Count() == 0)
            {
                MessageBox.Show("Select one or more sensors...", "Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (listCDTIDs.Count() == 0)
            {
                MessageBox.Show("Select one or more Data Channel Types...", "Alert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            IbrahimForm form = new IbrahimForm();
            form.StructureID = StructureId;
            form.dbContext = dbContext;
            form.elementsIDs = listIDs.ToList();
            form.ChannelsDataTypeIDs = listCDTIDs.ToList();
            form.ShowDialog();

       

    

    }
    }
}
