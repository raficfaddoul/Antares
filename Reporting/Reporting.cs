using System.Collections;
using AliantWebS.Models;
using AliantWebS.Data;
using System.Drawing;
using ExifLibrary;
using System.Text.Json;
using AliantWebS.Data;

//raficusing Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;
using System.Diagnostics;
using System.Reflection;
using System;
using Microsoft.Office.Interop.Word;
using System.Reflection.Metadata;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Reporting
{
    public class Reporting
    {

        AliantWebS.Data.EntitiesService entitiesService;


        public Reporting(object ob, AliantWebS.Data.EntitiesService entitiesService)
        {
            this.entitiesService = entitiesService;

            if (ob is Structure)
            {
                ReportStructure(ob);
            }
            else if (ob is Site)
            {
                ReportSite(ob);
            }

            else
            {
                List<Site> ls = (List<Site>)ob;

                foreach (Site s in ls) 
                {
                    ReportSite(s);
                }

            }




        }


        private void ReportSite(object ob)
        {
            Site site = (Site)ob;
           
            entitiesService.LoadCollection(site, "Structures");


            foreach (Structure str in site.Structures)
            {
                entitiesService.LoadCollection(str, "Elements");
                entitiesService.LoadReference(str, "StructureType");
                foreach (Element el in str.Elements)
                {

                    entitiesService.LoadCollection(el, "Defects");
                    entitiesService.LoadReference(el, "ElementType");
                    foreach (Defect def in el.Defects)
                    {
                        entitiesService.LoadReference(def, "DefectType");
                    }
                }
            }

            //  RapportWord(structure);
            //RapportWordFromDotx(site);
        }
        private void ReportStructure(object ob)
        {
            Structure structure = (Structure)ob;
  
            entitiesService.LoadCollection(structure, "Elements");
            entitiesService.LoadReference(structure, "StructureType");

            foreach (Element el in structure.Elements)
            {
                entitiesService.LoadCollection(el, "Defects");
                entitiesService.LoadReference(el, "ElementType");
                foreach (Defect def in el.Defects)
                {
                    entitiesService.LoadReference(def, "DefectType");
                }
            }

            //  RapportWord(structure);
            RapportWordFromDotx(structure);
        }


        private async Task<List<string>> GetPathForImages(object selectedObject, List<Element> ElementsToDispay)
        {
            List<string> paths = new List<string>();
            if (ElementsToDispay != null)
            {
                foreach (Element el in ElementsToDispay)
                {
                    paths.AddRange(await FileManagementService.GetPathForFilesAsync(el, false));
                }
            }
            else
            {
                paths = await FileManagementService.GetPathForFilesAsync(selectedObject, false);
            }

            return paths;
        }

        public async System.Threading.Tasks.Task Tagging(string path, List<Defect> DefectList)
        {
            List<Tag> tags = new List<Tag>();


            using (Image image = Image.FromFile(path))
            {



                ExifLibrary.ImageFile file;

                try
                {
                    file = ExifLibrary.ImageFile.FromFile(path);
                }
                catch
                {
                    throw new Exception();

                }

                var reee = file.Properties.Get(ExifTag.ImageDescription);
                if (reee == null)
                {
                    image.Save("c:\\Temp\\tagged.jpg");
                    return;
                }


                try
                {
                    var vv = JsonSerializer.Deserialize<List<Tag>>(reee?.ToString());
                    tags = vv;
                }
                catch
                {
                    throw new Exception();
                }

                List<Guid> defectIdList;

                if (DefectList != null)
                {
                    defectIdList = DefectList?.Select(x => x.DefectId).ToList();
                }
                else
                {
                    defectIdList = tags.Select(x => x.defectID).ToList();
                    DefectList = await entitiesService.GetFilteredEntitiesAsync<Defect>(x => defectIdList.Contains(x.DefectId));
                }



                // Font font = new Font("Arial", 20, FontStyle.Italic, GraphicsUnit.Pixel);
                Color color = Color.FromArgb(255, 255, 0, 0);

                SolidBrush brush = new SolidBrush(color);
                Graphics graphics = Graphics.FromImage(image);
                //   StringFormat sf = new StringFormat();
                //  sf.Alignment = StringAlignment.Center;
                //  sf.LineAlignment = StringAlignment.Center;

                // Pen pen = new Pen(brush);




                foreach (Tag t in tags)
                {
                    if ((defectIdList is null) || (defectIdList.Contains(t.defectID)))
                    {
                        //   Point atpoint = new Point((int)t.x * image.Width / 100, (int)t.y * image.Height / 100);

                        //  SystemIcons.

                        DefectType dft = DefectList.Find(x => x.DefectId == t.defectID).DefectType;

                        if (dft.Icon == null) continue;
                        var ms = new MemoryStream(dft.Icon);

                        Bitmap iconImage = (Bitmap)Image.FromStream(ms);


                        // Bitmap iconImage = (Bitmap)Bitmap.FromFile("Icons\\myFirstIcon.bmp");

                        Icon icon = Icon.FromHandle(iconImage.GetHicon());





                        graphics.DrawIcon(icon, 6 + (int)t.x * image.Width / 100, 6 + (int)t.y * image.Height / 100);


                        //       .DrawString("Madan S B", font, brush, atpoint, sf);
                    }

                }

                graphics.Dispose();
                brush.Dispose();

                image.Save("c:\\Temp\\tagged.jpg");
            }
        }



        public void RapportWordFromDotx(Structure structure)
        {
            object missing = Type.Missing;
            object oMissing = System.Reflection.Missing.Value;
            object oEndOfDoc = "\\endofdoc";
            //OBJECTS OF FALSE AND TRUE
            Object oTrue = true;
            Object oFalse = false;


            Word._Application word_app = new Word.Application();

            Object oTemplatePath = "C:\\Alaa\\C I D E C O\\Affaires\\2023P01 AC Balcons EHC\\R23P0101_A_EHC - Modele.dotx";
            Word.Document oWordDoc = word_app.Documents.Add(ref oTemplatePath, ref oMissing, ref oMissing, ref oMissing);
            int iTotalFields = 0;
            foreach (Word.Field myMergeField in oWordDoc.Fields)
            {
                iTotalFields++;
                Word.Range rngFieldCode = myMergeField.Code;
                String fieldText = rngFieldCode.Text;
                // ONLY GETTING THE MAILMERGE FIELDS
                if (fieldText.StartsWith(" MERGEFIELD"))
                {
                    // THE TEXT COMES IN THE FORMAT OF
                    // MERGEFIELD  MyFieldName  \\* MERGEFORMAT
                    // THIS HAS TO BE EDITED TO GET ONLY THE FIELDNAME "MyFieldName"
                    Int32 endMerge = fieldText.IndexOf("\\");
                    Int32 fieldNameLength = fieldText.Length - endMerge;
                    String fieldName = fieldText.Substring(11, endMerge - 11);
                    // GIVES THE FIELDNAMES AS THE USER HAD ENTERED IN .dot FILE
                    fieldName = fieldName.Trim();
                    // **** FIELD REPLACEMENT IMPLEMENTATION GOES HERE ****//
                    // THE PROGRAMMER CAN HAVE HIS OWN IMPLEMENTATIONS HERE
                    if (fieldName == "MyField")
                    {
                        myMergeField.Select();
                        word_app.Selection.TypeText("This Text Replaces the Field in the Template");
                    }
                }
            }

            tableDefauts(structure, oWordDoc, oMissing);

            //THE LOCATION WHERE THE FILE NEEDS TO BE SAVED
            string nom = structure.Name;
            Object oSaveAsFile = (Object)"C:\\Temp\\R23P0101_A_EHC - " + "nom" + ".docx";
            oWordDoc.SaveAs(ref oSaveAsFile, ref oMissing, ref oMissing, ref oMissing,
                ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing,
                ref oMissing, ref oMissing, ref oMissing, ref oMissing, ref oMissing,
                ref oMissing, ref oMissing);
            //CLOSING THE FILE
            oWordDoc.Close(ref oFalse, ref oMissing, ref oMissing);
            //QUITTING THE APPLICATION
            word_app.Quit(ref oMissing, ref oMissing, ref oMissing);
        }


            public void RapportWord(Structure structure)
        {

   /*         Process.Start(@"C:\Alaa\_Outils\MiniRadViewer.exe");

            IntPtr handle = IntPtr.Zero;
            Process[] localAll = Process.GetProcesses();
            foreach (Process p in localAll)
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    ProcessModule pm = pm = p.MainModule;
                    if (pm != null && p.MainModule.FileName.Contains("RadView"))
                        handle = p.MainWindowHandle;
                }
            }
            if (handle == IntPtr.Zero)
            {
                Console.WriteLine("Not found");
                return;
            }
   */

            Word._Application word_app = new Word.Application();

            // Make Word visible (optional).
            word_app.Visible = true;

            // Create the Word document.
            object missing = Type.Missing;
            object oMissing = System.Reflection.Missing.Value;
            object oEndOfDoc = "\\endofdoc"; /* \endofdoc is a predefined bookmark */
            Word._Document word_doc = word_app.Documents.Add(ref missing, ref missing, ref missing, ref missing);

            PagedeGarde(word_doc, missing);
            PagedeReferences(word_doc, missing);
            tableClassement(word_doc, missing);
            Preconisations(word_doc, missing);

            // Titre
            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Text = "Traces géoradar";
            object style_name = "Titre 1";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 16;
            para.Range.InsertParagraphAfter();

            // Add more text.
            para.Range.Text = "Le tableau ci-dessous fournit les traces géoradar réalisées au cours de cette étude.";
            para.Range.Font.Name = "HelveticaNeue MediumCond";
            para.Range.Font.Size = 12;
            para.Range.InsertParagraphAfter();

            Word.Table newTable;
            Word.Range wrdRng = word_doc.Bookmarks.get_Item(ref oEndOfDoc).Range;
            newTable = word_doc.Tables.Add(wrdRng, structure.Elements.Count(), 2, ref oMissing, ref oMissing);
            newTable.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            newTable.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            newTable.AllowAutoFit = true;

            newTable.Cell(1, 1).Range.Text = "No";
            newTable.Cell(1, 2).Range.Text = "Trace";
            newTable.Rows.Add();

            int irow = 1;
            foreach (Element el in structure.Elements)
            {
                foreach (Defect def in el.Defects)
                {
                    irow++;
                    string nom = el.Name;
                    newTable.Cell(irow, 1).Range.Text = nom;
                    newTable.Cell(irow, 2).Range.Text = def.DefectType.Name + "\r\n" + def.Description;

                }

                /*                Word.InlineShape inline_shape = newTable.Cell(irow, 2).Range.InlineShapes.AddPicture(fi.FullName, ref missing, ref missing, ref missing);
                                // Format the picture.
                                Word.Shape shape = inline_shape.ConvertToShape();

                                // Scale uniformly
                                shape.LockAspectRatio = Microsoft.Office.Core.MsoTriState.msoTrue;
                                shape.ScaleHeight(75 / shape.Width, Microsoft.Office.Core.MsoTriState.msoTrue, Microsoft.Office.Core.MsoScaleFrom.msoScaleFromTopLeft);

                                // Figure en ligne.
                                shape.WrapFormat.Type = Word.WdWrapType.wdWrapInline;

                  */
                //      newTable.Rows.Add();
            }

            object filename = Path.GetFullPath(@"c:\Temp\RapportBalcon-.docx");
            word_doc.SaveAs(ref filename, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing,
                ref missing);

            object save_changes = false;
            word_doc.Close(ref save_changes, ref missing, ref missing);
            word_app.Quit(ref save_changes, ref missing, ref missing);


        }

        private void PagedeGarde(Word._Document word_doc, object missing)
        {
            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Font.Size = 12;
            para.Range.Font.Name = "HelveticaNeue MediumCond";

            para.Range.Text = "Numéro d’affaire: 2023P0101";
            para.Range.InsertParagraphAfter();
            para.Range.Text = "Date: 05 / 02 / 2023";
            para.Range.InsertParagraphAfter();
            para.Range.Text = "Indice: A";
            para.Range.InsertParagraphAfter();

            object style_name = "Titre 1";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 28;
            para.Range.Font.Name = "HelveticaNeue MediumCond";

            para.Range.Text = "Rapport d'étude structure";
            para.Range.InsertParagraphAfter();
            para.Range.Text = "Inspection visuelle des balcons de la résidence XXX";
            para.Range.InsertParagraphAfter();
            para.Range.Text = "Etude réalisée pour:";
            para.Range.InsertParagraphAfter();
            para.Range.Text = "Espace Habitat Construction";
            para.Range.InsertParagraphAfter();

            word_doc.Words.Last.InsertBreak(Word.WdBreakType.wdPageBreak);

        }

        private void PagedeReferences(Word._Document word_doc, object missing)
        {
            object oMissing = System.Reflection.Missing.Value;
            object oEndOfDoc = "\\endofdoc"; /* \endofdoc is a predefined bookmark */

            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);

            Word.Table tableClient;
            Word.Range wrdRng = word_doc.Bookmarks.get_Item(ref oEndOfDoc).Range;
            tableClient = word_doc.Tables.Add(wrdRng, 3, 2, ref oMissing, ref oMissing);
            tableClient.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableClient.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableClient.AllowAutoFit = true;

            tableClient.Cell(1, 1).Range.Text = "Nom";
            tableClient.Cell(1, 2).Range.Text = "Espace Habitat Construction";

            tableClient.Cell(2, 1).Range.Text = "Adresse";
            tableClient.Cell(2, 2).Range.Text = "15 Rue Chanoinesse, 75004 Paris";

            tableClient.Cell(3, 1).Range.Text = "Interlocuteur";
            tableClient.Cell(3, 2).Range.Text = "";

            tableClient.Rows.Add();

            para.Range.InsertParagraphAfter();


            para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Font.Name = "C I D E C O";
            para.Range.Font.Name = "A F F A I R E";
            object style_name = "Titre 1";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 16;
            para.Range.InsertParagraphAfter();




            para = word_doc.Paragraphs.Add(ref missing);

            Word.Table tableAffaire;
            tableAffaire = word_doc.Tables.Add(wrdRng, 3, 2, ref oMissing, ref oMissing);
            tableAffaire.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableAffaire.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableAffaire.AllowAutoFit = true;

            tableAffaire.Cell(1, 1).Range.Text = "Numéro d’affaire";
            tableAffaire.Cell(1, 2).Range.Text = "2023P0101";

            tableAffaire.Cell(1, 1).Range.Text = "Intitulé";
            tableAffaire.Cell(1, 1).Range.Text = "Inspection visuelle des balcons";

            tableAffaire.Cell(1, 1).Range.Text = "Interlocuteur CIDECO";
            tableAffaire.Cell(1, 1).Range.Text = "S.BARNAY   simon.barnay @cideco.tech";

            tableAffaire.Rows.Add();
            para.Range.InsertParagraphAfter();


            para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Font.Name = "I N T E R V E N T I O N     S U R     S I T E";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 16;
            para.Range.InsertParagraphAfter();


            para = word_doc.Paragraphs.Add(ref missing);
            Word.Table tableSite;
            tableSite = word_doc.Tables.Add(wrdRng, 2, 2, ref oMissing, ref oMissing);
            tableSite.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableSite.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableSite.AllowAutoFit = true;

            tableSite.Cell(1, 1).Range.Text = "Responsable";
            tableSite.Cell(1, 2).Range.Text = "S. BARNAY";

            tableSite.Cell(2, 1).Range.Text = "Autres intervenants";
            tableSite.Cell(2, 2).Range.Text = "R. TIOURTIT et S.BARNAY";
            para.Range.InsertParagraphAfter();



            para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Font.Name = "R A P P O R T";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 16;
            para.Range.InsertParagraphAfter();

            para = word_doc.Paragraphs.Add(ref missing);
            Word.Table tableRapport;
            tableRapport = word_doc.Tables.Add(wrdRng, 4, 5, ref oMissing, ref oMissing);
            tableRapport.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableRapport.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableRapport.AllowAutoFit = true;

            tableRapport.Cell(1, 1).Range.Text = "Date";
            tableRapport.Cell(1, 2).Range.Text = "Indice";
            tableRapport.Cell(1, 3).Range.Text = "Observations / Modifications";
            tableRapport.Cell(1, 4).Range.Text = "Rédacteur";
            tableRapport.Cell(1, 5).Range.Text = "Relecteur";
            tableRapport.Cell(2, 1).Range.Text = "05/02/2023";
            tableRapport.Cell(2, 2).Range.Text = "A";
            tableRapport.Cell(2, 3).Range.Text = "Rapport initial";
            tableRapport.Cell(2, 4).Range.Text = "S. BARNAY";
            tableRapport.Cell(2, 5).Range.Text = "A.CHATEAUNEUF";
            para.Range.InsertParagraphAfter();


            word_doc.Words.Last.InsertBreak(Word.WdBreakType.wdPageBreak);

        }
        private void tableClassement(Word._Document word_doc, object missing)
        {
            object oMissing = System.Reflection.Missing.Value;
            object oEndOfDoc = "\\endofdoc"; /* \endofdoc is a predefined bookmark */

            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Text = "Le classement des balcons est effectué selon le tableau suivant :";
            para.Range.Font.Size = 12;
            para.Range.Font.Name = "HelveticaNeue MediumCond";
            para.Range.InsertParagraphAfter();



            Word.Table tableClasse;
            Word.Range wrdRng = word_doc.Bookmarks.get_Item(ref oEndOfDoc).Range;
            tableClasse = word_doc.Tables.Add(wrdRng, 7, 4, ref oMissing, ref oMissing);
            tableClasse.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableClasse.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableClasse.AllowAutoFit = true;

            tableClasse.Cell(1, 1).Range.Text = "Classe";
            tableClasse.Cell(1, 2).Range.Text = "Risques";
            tableClasse.Cell(1, 3).Range.Text = "Actions";
            tableClasse.Cell(1, 4).Range.Text = "";

            tableClasse.Cell(2, 1).Range.Text = "";
            tableClasse.Cell(2, 2).Range.Text = "";
            tableClasse.Cell(2, 3).Range.Text = "Court terme";
            tableClasse.Cell(2, 4).Range.Text = "Long terme";

            tableClasse.Cell(3, 1).Range.Text = "1";
            tableClasse.Cell(3, 2).Range.Text = "Aucun";
            tableClasse.Cell(3, 3).Range.Text = "Veille";
            tableClasse.Cell(3, 4).Range.Text = "Veille";

            tableClasse.Cell(4, 1).Range.Text = "2";
            tableClasse.Cell(4, 2).Range.Text = "Dégradations mineures, sans danger pour l’intégrité";
            tableClasse.Cell(4, 3).Range.Text = "Surveillance";
            tableClasse.Cell(4, 4).Range.Text = "Protection";

            tableClasse.Cell(5, 1).Range.Text = "3";
            tableClasse.Cell(5, 2).Range.Text = "Désordres structuraux, danger sans caractère d’urgence";
            tableClasse.Cell(5, 3).Range.Text = "Surveillance";
            tableClasse.Cell(5, 4).Range.Text = "Protection";

            tableClasse.Cell(6, 1).Range.Text = "4";
            tableClasse.Cell(6, 2).Range.Text = "Danger à court terme";
            tableClasse.Cell(6, 3).Range.Text = "Sécurisation, surveillance";
            tableClasse.Cell(6, 4).Range.Text = "Renforcement/Réhabilitation";

            tableClasse.Cell(7, 1).Range.Text = "5";
            tableClasse.Cell(7, 2).Range.Text = "Danger imminent";
            tableClasse.Cell(7, 3).Range.Text = "Sécurisation immédiate, fermeture";
            tableClasse.Cell(7, 4).Range.Text = "Renforcement\r\nReconstruction";


            word_doc.Words.Last.InsertBreak(Word.WdBreakType.wdPageBreak);

        }

        private void tableDefauts(Structure structure,Word.Document word_doc, object missing)
        {
            object oMissing = System.Reflection.Missing.Value;
            object oEndOfDoc = "\\endofdoc"; /* \endofdoc is a predefined bookmark */

            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Text = "Le tableau suivant indique les défauts observés dans les balcons de la structure";
            para.Range.Font.Size = 12;
            para.Range.Font.Name = "HelveticaNeue MediumCond";
            para.Range.InsertParagraphAfter();



            Word.Table tableDefauts;
            Word.Range wrdRng = word_doc.Bookmarks.get_Item(ref oEndOfDoc).Range;
            tableDefauts = word_doc.Tables.Add(wrdRng, 7, 3, ref oMissing, ref oMissing);
            tableDefauts.Borders.InsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableDefauts.Borders.OutsideLineStyle = Microsoft.Office.Interop.Word.WdLineStyle.wdLineStyleSingle;
            tableDefauts.AllowAutoFit = true;

            tableDefauts.Cell(1, 1).Range.Text = "Elément";
            tableDefauts.Cell(1, 2).Range.Text = "Photos";
            tableDefauts.Cell(1, 3).Range.Text = "Description";
            int iligne = 1;

            List<string> paths = null;
            Word.InlineShape inline_shape = null;
            foreach (Element el in structure.Elements)
            {
                entitiesService.LoadCollection(el, "Defects");
                entitiesService.LoadReference(el, "ElementType");

                iligne++;
                tableDefauts.Cell(iligne, 1).Range.Text = el.Name;
                
                paths = GetPathForImages(el, null).Result;
                for (int ifo = 0; ifo < paths.Count; ifo++)
                    if(paths.Count>0)
                {
                        
                    Tagging(paths[ifo], null).Wait();
                    inline_shape = tableDefauts.Cell(iligne, 2).Range.InlineShapes.AddPicture("c:\\Temp\\tagged.jpg", ref oMissing, ref oMissing, ref oMissing);
                }
                tableDefauts.Cell(iligne, 3).Range.Text = el.Description;

                iligne++;
                tableDefauts.Cell(iligne, 1).Range.Text = "Défaut";
                tableDefauts.Cell(iligne, 2).Range.Text = "Photos";
                tableDefauts.Cell(iligne, 3).Range.Text = "Description";

                foreach (Defect def in el.Defects)
                {
                    entitiesService.LoadReference(def, "DefectType");

                    iligne++;
                    
                    tableDefauts.Cell(iligne, 1).Range.Text = def.DefectType.Name;
                    paths = GetPathForImages(el, null).Result;
                    for (int ifo = 0; ifo < paths.Count; ifo++)
                    {
                        if (paths.Count != 0) Tagging(paths[0], null).Wait();
                        inline_shape = tableDefauts.Cell(iligne, 2).Range.InlineShapes.AddPicture("c:\\Temp\\tagged.jpg", ref oMissing, ref oMissing, ref oMissing);
                        // Format the picture.
                        Word.Shape shape = inline_shape.ConvertToShape();

                        // Scale uniformly
                        //rafic shape.LockAspectRatio = Microsoft.Office.Core.MsoTriState.msoTrue;
                        //rafic shape.ScaleHeight(75 / shape.Width, Microsoft.Office.Core.MsoTriState.msoTrue, Microsoft.Office.Core.MsoScaleFrom.msoScaleFromTopLeft);

                        // Figure en ligne.
                        shape.WrapFormat.Type = WdWrapType.wdWrapInline;
                    }
                    tableDefauts.Cell(iligne, 3).Range.Text = def.Description;
                    
                }
            }

            word_doc.Words.Last.InsertBreak(Word.WdBreakType.wdPageBreak);

        }

        private void Preconisations(Word._Document word_doc, object missing)
        {
            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);
            object style_name = "Titre 1";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 16;
            para.Range.Font.Name = "HelveticaNeue MediumCond";

            para.Range.Text = "2.Préconisations";
            para.Range.InsertParagraphAfter();


            para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Font.Size = 12;
            para.Range.Font.Name = "HelveticaNeue MediumCond";

            para.Range.Text =

            "Les préconisations proposées sont de deux natures:\r\nActions de conservation:\r\n"
            + "Pour assurer la pérennité des balcons,\r\n"
            + "\t• surveillance des fissures\r\n"
            + "\t• installer un profil PVC ou aluminium au niveau de la goutte d’eau.\r\n"
            + "\r\n\r\n"
            + "Actions de réhabilitation:\r\n"
            + "Les solutions à mettre en place dès que possible pour limiter les dégradations et assurer la sécurité:\r\n"
            + "\t• déposer la couche d’étanchéité\r\n"
            + "\t• curer le béton sur quelques centimètres(i.e.au-delà des aciers secondaires) au niveau des nez de dalle ;\r\n"
            + "\t• nettoyer les aciers à la brosse métallique et avec un produit adapté ;\r\n"
            + "\t• dédoublement des aciers secondaires dans les zones fortement corrodées; \r\n"
            + "\t• appliquer un primaire d'accrochage ;\r\n"
            + "\t• appliquer un mortier de réparation, après avoir posé des ancrages métalliques ;\r\n"
            + "\t• réaliser une nouvelle couche d'étanchéité ;\r\n"

            + "\r\n\r\n"
            + "Recommandations:\r\n"
            + "Les investigations complémentaires nécessaires à la compréhension des désordres constatés afin de proposer des solutions de remise en état des bâtiments.\r\n";

            para.Range.InsertParagraphAfter();
            word_doc.Words.Last.InsertBreak(Word.WdBreakType.wdPageBreak);
        }
        private void ObjetEtude(Word._Document word_doc, object missing)
        {
            Word.Paragraph para = word_doc.Paragraphs.Add(ref missing);
            object style_name = "Titre 1";
            para.Range.set_Style(ref style_name);
            para.Range.Font.Size = 16;
            para.Range.Font.Name = "HelveticaNeue MediumCond";

            para.Range.Text = "3.Objet de l’étude";
            para.Range.InsertParagraphAfter();


            para = word_doc.Paragraphs.Add(ref missing);
            para.Range.Font.Size = 12;
            para.Range.Font.Name = "HelveticaNeue MediumCond";

            para.Range.Text =

            "Espace Habitat Construction souhaite réaliser un audit préventif sur les balcons et garde-corps de son parc immobilier datant du début des années 1990.\r\n"
            + "L’objectif est dans un premier temps, de réaliser une visite d’inspection sur chacune des 80 résidences faisant l’objet de cet accord-cadre, afin d’inventorier et de dénombrer les balcons et garde-corps, et de déterminer leur état sanitaire au moyen d’une inspection visuelle et d’un relevé des désordres. Cette première visite technique permettra de qualifier les désordres observés et de les classer selon leur degré de criticité et ainsi avertir immédiatement le maître d’ouvrage en cas de péril imminent, tout en ne dévoilant aucune information sensible aux usagers et habitants des bâtiments.\r\n"
            + "Cette première phase d’inspection permettra également lorsque cela s’avérera nécessaire, de préconiser des investigations complémentaires afin d’obtenir des informations plus poussées sur l’état des structures de balcons et garde - corps et ainsi pouvoir préconiser des éventuelles solutions de réparation.Ces prestations complémentaires seront réalisées sur la base de bons de commande.";

            para.Range.InsertParagraphAfter();
            word_doc.Words.Last.InsertBreak(Word.WdBreakType.wdPageBreak);
        }

    }
}