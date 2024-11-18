namespace AliantDesktop
{
    partial class IdentificationForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            dataGridViewSensors = new DataGridView();
            NameColumn = new DataGridViewTextBoxColumn();
            ActionColumn = new DataGridViewCheckBoxColumn();
            ChannelDataTypeDataGridView = new DataGridView();
            ChannelDataTypeColumn = new DataGridViewTextBoxColumn();
            CDTActionColumn = new DataGridViewCheckBoxColumn();
            Ibrahimbutton = new Button();
            ((System.ComponentModel.ISupportInitialize)dataGridViewSensors).BeginInit();
            ((System.ComponentModel.ISupportInitialize)ChannelDataTypeDataGridView).BeginInit();
            SuspendLayout();
            // 
            // dataGridViewSensors
            // 
            dataGridViewSensors.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridViewSensors.Columns.AddRange(new DataGridViewColumn[] { NameColumn, ActionColumn });
            dataGridViewSensors.Location = new Point(41, 38);
            dataGridViewSensors.Name = "dataGridViewSensors";
            dataGridViewSensors.RowHeadersWidth = 51;
            dataGridViewSensors.Size = new Size(344, 429);
            dataGridViewSensors.TabIndex = 0;
            // 
            // NameColumn
            // 
            NameColumn.DataPropertyName = "Name";
            NameColumn.HeaderText = "Sensing Device";
            NameColumn.MinimumWidth = 6;
            NameColumn.Name = "NameColumn";
            NameColumn.Width = 125;
            // 
            // ActionColumn
            // 
            ActionColumn.HeaderText = "Action";
            ActionColumn.MinimumWidth = 6;
            ActionColumn.Name = "ActionColumn";
            ActionColumn.Width = 125;
            // 
            // ChannelDataTypeDataGridView
            // 
            ChannelDataTypeDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            ChannelDataTypeDataGridView.Columns.AddRange(new DataGridViewColumn[] { ChannelDataTypeColumn, CDTActionColumn });
            ChannelDataTypeDataGridView.Location = new Point(511, 44);
            ChannelDataTypeDataGridView.Name = "ChannelDataTypeDataGridView";
            ChannelDataTypeDataGridView.RowHeadersWidth = 51;
            ChannelDataTypeDataGridView.Size = new Size(300, 387);
            ChannelDataTypeDataGridView.TabIndex = 1;
            // 
            // ChannelDataTypeColumn
            // 
            ChannelDataTypeColumn.DataPropertyName = "Name";
            ChannelDataTypeColumn.HeaderText = "ChannelDataType";
            ChannelDataTypeColumn.MinimumWidth = 6;
            ChannelDataTypeColumn.Name = "ChannelDataTypeColumn";
            ChannelDataTypeColumn.Width = 125;
            // 
            // CDTActionColumn
            // 
            CDTActionColumn.HeaderText = "Action";
            CDTActionColumn.MinimumWidth = 6;
            CDTActionColumn.Name = "CDTActionColumn";
            CDTActionColumn.Resizable = DataGridViewTriState.True;
            CDTActionColumn.SortMode = DataGridViewColumnSortMode.Automatic;
            CDTActionColumn.Width = 125;
            // 
            // Ibrahimbutton
            // 
            Ibrahimbutton.Location = new Point(920, 55);
            Ibrahimbutton.Name = "Ibrahimbutton";
            Ibrahimbutton.Size = new Size(162, 29);
            Ibrahimbutton.TabIndex = 2;
            Ibrahimbutton.Text = "Ibrahim";
            Ibrahimbutton.UseVisualStyleBackColor = true;
            Ibrahimbutton.Click += Ibrahimbutton_Click;
            // 
            // IdentificationForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1164, 517);
            Controls.Add(Ibrahimbutton);
            Controls.Add(ChannelDataTypeDataGridView);
            Controls.Add(dataGridViewSensors);
            Name = "IdentificationForm";
            Text = "IdentificationForm";
            ((System.ComponentModel.ISupportInitialize)dataGridViewSensors).EndInit();
            ((System.ComponentModel.ISupportInitialize)ChannelDataTypeDataGridView).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private DataGridView dataGridViewSensors;
        private DataGridViewTextBoxColumn NameColumn;
        private DataGridViewCheckBoxColumn ActionColumn;
        private DataGridView ChannelDataTypeDataGridView;
        private DataGridViewTextBoxColumn ChannelDataTypeColumn;
        private DataGridViewCheckBoxColumn CDTActionColumn;
        private Button Ibrahimbutton;
    }
}