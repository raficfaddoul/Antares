using AliantWebS.Models;
using Microsoft.EntityFrameworkCore;

namespace AliantDesktop
{
    public partial class Form1 : Form
    {
        IdentificationForm idf;
        Guid StructureId;
        AliantWebContext dbContext;

        public Form1()
        {
            InitializeComponent();


            StructureId = new Guid("37b092ba-ed5c-ee11-8daf-5820b17cefc4");

            this.Load += IdentificationForm_Load;
      
        }

        private async void IdentificationForm_Load(object sender, EventArgs e)
        {
            string connectionString = "Server=localhost;Database=AliantWeb;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True; Encrypt=False;";
            var optionsBuilder = new DbContextOptionsBuilder<AliantWebContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());

            // Create an instance of your DbContext
             dbContext = new AliantWebContext(optionsBuilder.Options);

            idf = new IdentificationForm();
            idf.StructureId = StructureId;
            idf.dbContext = dbContext;
            idf.ShowDialog();
        }
    }
}
