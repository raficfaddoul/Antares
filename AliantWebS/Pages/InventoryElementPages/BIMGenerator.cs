using System.Collections.Generic;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.RepresentationResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using System.Linq;
using Xbim.Ifc4.GeometricModelResource;
using System;
using Xbim.Ifc4.GeometricConstraintResource;
using Xbim.Ifc4.ProfileResource;
using Xbim.Ifc4.MaterialResource;
using Xbim.Ifc4.PresentationOrganizationResource;
//using Microsoft.Office.Interop.Word;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.MeasureResource;
using AliantWebS.Data;
using AliantWebS.Models;
using Xbim.IO.Xml.BsConf;
using System.Threading.Tasks;
using System.IO;
using Structural;
using Radzen.Blazor.Rendering;
using Xbim.Ifc4.PresentationAppearanceResource;
using Microsoft.Isam.Esent.Interop;

namespace AliantWebS.Pages.InventoryElementPages
{
    public class BIMGenerator
    {
        EntitiesService entitiesService;
        AppData appData;

        List<Element> poutres;

        public BIMGenerator(EntitiesService entitiesService, AppData appData)
        {
            this.entitiesService = entitiesService;
            this.appData = appData;
        }

        List<(double b, double h, double x1, double y1, double z1, double x2, double y2, double z2, Element poutre)> beams = new List<(double b, double h, double x1, double y1, double z1, double x2, double y2, double z2, Element poutre)>();


        public async Task Create()
        {


            poutres = await entitiesService.GetFilteredEntitiesAsync<Element>(x => (x.StructureId == appData.Structure.StructureId) && ((x.ElementType?.Name == "Beam3d") ||(x.ElementType?.Name == "Câble") ||(x.ElementType?.Name == "Treillis3d")), "ElementType", "AttributeElements");
            AttributeType tn1 = (await entitiesService.GetFilteredEntitiesAsync<AttributeType>(x => x.Name == "Noeud1")).FirstOrDefault();
            AttributeType tn2 = (await entitiesService.GetFilteredEntitiesAsync<AttributeType>(x => x.Name == "Noeud2")).FirstOrDefault();

            foreach (var p in poutres)
            {
                var an1 = p.AttributeElements.Where(x => x.AttributeType.AttributeTypeId == tn1.AttributeTypeId).FirstOrDefault();
                var an2 = p.AttributeElements.Where(x => x.AttributeType.AttributeTypeId == tn2.AttributeTypeId).FirstOrDefault();

                if ((an1 == null) || (an2 == null))
                {
                    continue;
                }

                var n1 = (await entitiesService.GetFilteredEntitiesAsync<Element>(x => (x.StructureId == appData.Structure.StructureId) && (x.ElementType?.Name == "Noeud3d") && (an1.Value == x.Name))).FirstOrDefault();
                var n2 = (await entitiesService.GetFilteredEntitiesAsync<Element>(x => (x.StructureId == appData.Structure.StructureId) && (x.ElementType?.Name == "Noeud3d") && (an2.Value == x.Name))).FirstOrDefault();

                var x1 = double.Parse(n1.AttributeElements.Where(x => x.AttributeType.Name == "X").FirstOrDefault().Value);
                var y1 = double.Parse(n1.AttributeElements.Where(x => x.AttributeType.Name == "Y").FirstOrDefault().Value);
                var z1 = double.Parse(n1.AttributeElements.Where(x => x.AttributeType.Name == "Z").FirstOrDefault().Value);

                var x2 = double.Parse(n2.AttributeElements.Where(x => x.AttributeType.Name == "X").FirstOrDefault().Value);
                var y2 = double.Parse(n2.AttributeElements.Where(x => x.AttributeType.Name == "Y").FirstOrDefault().Value);
                var z2 = double.Parse(n2.AttributeElements.Where(x => x.AttributeType.Name == "Z").FirstOrDefault().Value);

                var b = double.Parse(p.AttributeElements.Where(x => x.AttributeType.Name == "b").FirstOrDefault().Value);
                var h = double.Parse(p.AttributeElements.Where(x => x.AttributeType.Name == "h").FirstOrDefault().Value);


                beams.Add((b * 1000, h * 1000, x1 * 1000, y1 * 1000, z1 * 1000, x2 * 1000, y2 * 1000, z2 * 1000, p));

            }


            //beams = new List<(double x1, double y1, double z1, double x2, double y2, double z2)>();
            //beams.Add((0, 0, 0, 1000, 0, 0));
            //beams.Add((1000, 0, 0, 1000, 2000, 0));



            using (var model = CreateandInitModel("HelloWall"))
            {
                if (model != null)
                {
                    IfcBuilding building = CreateBuilding(model, "Default Building");
                    int i = 0;
                    foreach (var b in beams)
                    {
                        if (i == 500)
                        {
                            int k = 1;
                        }
                        IfcBeam beam = CreateBeam(model, b.b, b.h, b.x1, b.y1, b.z1, b.x2, b.y2, b.z2, b.poutre);

                        //   IfcWallStandardCase wall = CreateWall(model, 4000, 300, 10000);

                        //          if (wall != null) AddPropertiesToWall(model, wall);
                        using (var txn = model.BeginTransaction("Add Beam" + i.ToString()))
                        {
                            building.AddElement(beam);
                            txn.Commit();
                        }
                        i++;

                    }


                    try
                    {
                        await entitiesService.UpdateEntitesAsync(poutres);
                        Console.WriteLine("Standard Wall successfully created....");
                        string structurePath = FileManagementService.GetFilesLocation(appData.Structure, @"wwwroot\data").Item1;
                        if (!Directory.Exists(structurePath))
                        {
                            Directory.CreateDirectory(structurePath);
                        }

                        List<string> paths = await FileManagementService.GetPathForFilesAsync(appData.Structure, false, @"wwwroot/data");
                        List<string> FilePath = paths.Where(x => (x.ToLower().Contains(".wexbim")) || (x.ToLower().Contains(".ifc"))).ToList();

                        foreach (var pa in FilePath)
                        {
                            if (File.Exists(pa))
                            {
                                File.Delete(pa);
                            }
                        }

                        var fileName = System.IO.Path.Combine(structurePath, appData.Structure.Name + ".ifc");
                        model.SaveAs(fileName, StorageType.Ifc);
                        await InventoryElementPages.BIMComponent.transformToWexBim(fileName);
                        Console.WriteLine("HelloWallIfc4.ifc has been successfully written");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to save HelloWall.ifc");
                        Console.WriteLine(e.Message);
                    }

                }
                else
                {
                    Console.WriteLine("Failed to initialise the model");
                }
            }




        }

        static private IfcWallStandardCase CreateWall(IfcStore model, double length, double width, double height, double x, double y, double directionX, double directionY)
        {
            //
            //begin a transaction
            using (var txn = model.BeginTransaction("Create Wall"))
            {
                var wall = model.Instances.New<IfcWallStandardCase>();
                wall.Name = "A Standard rectangular wall";

                //represent wall as a rectangular profile
                var rectProf = model.Instances.New<IfcRectangleProfileDef>();
                rectProf.ProfileType = IfcProfileTypeEnum.AREA;
                rectProf.XDim = width;
                rectProf.YDim = length;

                var insertPoint = model.Instances.New<IfcCartesianPoint>();
                insertPoint.SetXY(0, 0); //insert at arbitrary position
                rectProf.Position = model.Instances.New<IfcAxis2Placement2D>();
                rectProf.Position.Location = insertPoint;

                //model as a swept area solid
                var body = model.Instances.New<IfcExtrudedAreaSolid>();
                body.Depth = height;
                body.SweptArea = rectProf;
                body.ExtrudedDirection = model.Instances.New<IfcDirection>();
                body.ExtrudedDirection.SetXYZ(0, 0, 1);

                //parameters to insert the geometry in the model
                var origin = model.Instances.New<IfcCartesianPoint>();
                origin.SetXYZ(x, y, 0);
                body.Position = model.Instances.New<IfcAxis2Placement3D>();
                body.Position.Location = origin;

                //Create a Definition shape to hold the geometry
                var shape = model.Instances.New<IfcShapeRepresentation>();
                var modelContext = model.Instances.OfType<IfcGeometricRepresentationContext>().FirstOrDefault();
                shape.ContextOfItems = modelContext;
                shape.RepresentationType = "SweptSolid";
                shape.RepresentationIdentifier = "Body";
                shape.Items.Add(body);

                //Create a Product Definition and add the model geometry to the wall
                var rep = model.Instances.New<IfcProductDefinitionShape>();
                rep.Representations.Add(shape);
                wall.Representation = rep;

                //now place the wall into the model
                var lp = model.Instances.New<IfcLocalPlacement>();
                var ax3D = model.Instances.New<IfcAxis2Placement3D>();
                ax3D.Location = origin;
                ax3D.RefDirection = model.Instances.New<IfcDirection>();
                ax3D.RefDirection.SetXYZ(directionX, directionY, 0);
                ax3D.Axis = model.Instances.New<IfcDirection>();
                ax3D.Axis.SetXYZ(0, 0, 1);
                lp.RelativePlacement = ax3D;
                wall.ObjectPlacement = lp;


                // Where Clause: The IfcWallStandard relies on the provision of an IfcMaterialLayerSetUsage 
                var ifcMaterialLayerSetUsage = model.Instances.New<IfcMaterialLayerSetUsage>();
                var ifcMaterialLayerSet = model.Instances.New<IfcMaterialLayerSet>();
                var ifcMaterialLayer = model.Instances.New<IfcMaterialLayer>();
                ifcMaterialLayer.LayerThickness = 10;
                ifcMaterialLayerSet.MaterialLayers.Add(ifcMaterialLayer);
                ifcMaterialLayerSetUsage.ForLayerSet = ifcMaterialLayerSet;
                ifcMaterialLayerSetUsage.LayerSetDirection = IfcLayerSetDirectionEnum.AXIS2;
                ifcMaterialLayerSetUsage.DirectionSense = IfcDirectionSenseEnum.NEGATIVE;
                ifcMaterialLayerSetUsage.OffsetFromReferenceLine = 150;

                // Add material to wall
                var material = model.Instances.New<IfcMaterial>();
                material.Name = "some material";
                var ifcRelAssociatesMaterial = model.Instances.New<IfcRelAssociatesMaterial>();
                ifcRelAssociatesMaterial.RelatingMaterial = material;
                ifcRelAssociatesMaterial.RelatedObjects.Add(wall);

                ifcRelAssociatesMaterial.RelatingMaterial = ifcMaterialLayerSetUsage;

                // IfcPresentationLayerAssignment is required for CAD presentation in IfcWall or IfcWallStandardCase
                var ifcPresentationLayerAssignment = model.Instances.New<IfcPresentationLayerAssignment>();
                ifcPresentationLayerAssignment.Name = "some ifcPresentationLayerAssignment";
                ifcPresentationLayerAssignment.AssignedItems.Add(shape);


                // linear segment as IfcPolyline with two points is required for IfcWall
                var ifcPolyline = model.Instances.New<IfcPolyline>();
                var startPoint = model.Instances.New<IfcCartesianPoint>();
                startPoint.SetXY(0, 0);
                var endPoint = model.Instances.New<IfcCartesianPoint>();
                endPoint.SetXY(4000, 0);
                ifcPolyline.Points.Add(startPoint);
                ifcPolyline.Points.Add(endPoint);

                var shape2D = model.Instances.New<IfcShapeRepresentation>();
                shape2D.ContextOfItems = modelContext;
                shape2D.RepresentationIdentifier = "Axis";
                shape2D.RepresentationType = "Curve2D";
                shape2D.Items.Add(ifcPolyline);
                rep.Representations.Add(shape2D);
                txn.Commit();
                return wall;
            }
        }

        public static void CreateIfcFileWithBeams(List<(double x1, double y1, double z1, double x2, double y2, double z2)> beams)
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Your Company Name",
                ApplicationFullName = "Your Application Name",
                ApplicationIdentifier = "YourAppId",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Last Name",
                EditorsGivenName = "First Name",
                EditorsOrganisationName = "Your Org Name"
            };

            using (var model = IfcStore.Create(editor, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (var txn = model.BeginTransaction("Create Beams"))
                {
                    var context = model.Instances.OfType<IfcGeometricRepresentationContext>().FirstOrDefault();

                    foreach (var beam in beams)
                    {
                        var startPoint = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(beam.x1, beam.y1, beam.z1));
                        var endPoint = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(beam.x2, beam.y2, beam.z2));

                        // Calculate direction vector components
                        var dx = beam.x2 - beam.x1;
                        var dy = beam.y2 - beam.y1;
                        var dz = beam.z2 - beam.z1;
                        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                        // Normalize direction vector components
                        var normX = dx / length;
                        var normY = dy / length;
                        var normZ = dz / length;

                        var direction = model.Instances.New<IfcDirection>();
                        direction.SetXYZ(normX, normY, normZ);

                        // Create a local placement at the start point
                        var localPlacement = model.Instances.New<IfcLocalPlacement>();
                        var placementAxis = model.Instances.New<IfcAxis2Placement3D>();
                        placementAxis.Location = startPoint;
                        placementAxis.RefDirection = direction;
                        localPlacement.RelativePlacement = placementAxis;

                        // Define beam profile
                        var profile = model.Instances.New<IfcRectangleProfileDef>();
                        profile.ProfileType = IfcProfileTypeEnum.AREA;
                        profile.XDim = 0.2; // Width of the beam, e.g., 200 mm
                        profile.YDim = 0.4; // Depth of the beam, e.g., 400 mm
                        profile.Position = model.Instances.New<IfcAxis2Placement2D>();

                        // Extrude the profile to form the beam
                        var extrusion = model.Instances.New<IfcExtrudedAreaSolid>();
                        extrusion.Depth = length;
                        extrusion.ExtrudedDirection = model.Instances.New<IfcDirection>(d => d.SetXYZ(0, 0, 1)); // Typically extruding along the Z-axis in the profile's local space
                        extrusion.SweptArea = profile;

                        // Represent the shape of the beam
                        var shape = model.Instances.New<IfcShapeRepresentation>();
                        shape.ContextOfItems = context;
                        shape.RepresentationType = "SweptSolid";
                        shape.RepresentationIdentifier = "Body";
                        shape.Items.Add(extrusion);

                        // Create the beam element
                        var ifcBeam = model.Instances.New<IfcBeam>();
                        ifcBeam.ObjectPlacement = localPlacement;
                        ifcBeam.Representation = model.Instances.New<IfcProductDefinitionShape>(r => r.Representations.Add(shape));

                        // Optionally, set additional properties of the beam
                        ifcBeam.Name = "Structural Beam";
                        ifcBeam.Tag = "BeamTag"; // Example tag, customize as needed
                    }

                    // Commit the transaction to finalize the changes
                    txn.Commit();
                }

                // Save the model as an IFC file
                model.SaveAs("beams.ifc");
            }
        }

        static public void CreateIfcFileWithBeamso(List<(double x1, double y1, double z1, double x2, double y2, double z2)> beams)
        {
            var editor = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "Your Company Name",
                ApplicationFullName = "Your Application Name",
                ApplicationIdentifier = "YourAppId",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Last Name",
                EditorsGivenName = "First Name",
                EditorsOrganisationName = "Your Org Name"
            };

            using (var model = IfcStore.Create(editor, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel))
            {
                using (var txn = model.BeginTransaction("Create Beams"))
                {
                    var context = new Xbim3DModelContext(model);

                    foreach (var beam in beams)
                    {
                        var startPoint = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(beam.x1, beam.y1, beam.z1));
                        var endPoint = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(beam.x2, beam.y2, beam.z2));
                        var line = model.Instances.New<IfcPolyline>(l => l.Points.AddRange(new[] { startPoint, endPoint }));

                        var shape = model.Instances.New<IfcShapeRepresentation>();

                        var modelContext = model.Instances.OfType<IfcGeometricRepresentationContext>().FirstOrDefault();
                        shape.ContextOfItems = modelContext;

                        shape.RepresentationType = "Curve2D";
                        shape.RepresentationIdentifier = "Body";
                        shape.Items.Add(line);

                        var ifcBeam = model.Instances.New<IfcBeam>();
                        ifcBeam.Representation = model.Instances.New<IfcProductDefinitionShape>(r => r.Representations.Add(shape));
                    }

                    txn.Commit();
                }
                model.SaveAs("beams.ifc");
            }
        }

        private static IfcBuilding CreateBuilding(IfcStore model, string name)
        {
            using (var txn = model.BeginTransaction("Create Building"))
            {
                var building = model.Instances.New<IfcBuilding>();
                building.Name = name;

                building.CompositionType = IfcElementCompositionEnum.ELEMENT;
                var localPlacement = model.Instances.New<IfcLocalPlacement>();
                building.ObjectPlacement = localPlacement;
                var placement = model.Instances.New<IfcAxis2Placement3D>();
                localPlacement.RelativePlacement = placement;
                placement.Location = model.Instances.New<IfcCartesianPoint>(p => p.SetXYZ(0, 0, 0));
                //get the project there should only be one and it should exist
                var project = model.Instances.OfType<IfcProject>().FirstOrDefault();
                project?.AddBuilding(building);
                txn.Commit();
                return building;
            }
        }



        /// <summary>
        /// Sets up the basic parameters any model must provide, units, ownership etc
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns></returns>
        private static IfcStore CreateandInitModel(string projectName)
        {
            //first we need to set up some credentials for ownership of data in the new model
            var credentials = new XbimEditorCredentials
            {
                ApplicationDevelopersName = "xbim developer",
                ApplicationFullName = "Hello Wall Application",
                ApplicationIdentifier = "HelloWall.exe",
                ApplicationVersion = "1.0",
                EditorsFamilyName = "Team",
                EditorsGivenName = "xbim",
                EditorsOrganisationName = "xbim developer"
            };
            //now we can create an IfcStore, it is in Ifc4 format and will be held in memory rather than in a database
            //database is normally better in performance terms if the model is large >50MB of Ifc or if robust transactions are required

            var model = IfcStore.Create(credentials, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);

            //Begin a transaction as all changes to a model are ACID
            using (var txn = model.BeginTransaction("Initialise Model"))
            {

                //create a project
                var project = model.Instances.New<IfcProject>();
                //set the units to SI (mm and metres)
                project.Initialize(ProjectUnits.SIUnitsUK);
                project.Name = projectName;
                //now commit the changes, else they will be rolled back at the end of the scope of the using statement
                txn.Commit();
            }
            return model;

        }
        /// <summary>
        /// This creates a wall and it's geometry, many geometric representations are possible and extruded rectangular footprint is chosen as this is commonly used for standard case walls
        /// </summary>
        /// <param name="model"></param>
        /// <param name="length">Length of the rectangular footprint</param>
        /// <param name="width">Width of the rectangular footprint (width of the wall)</param>
        /// <param name="height">Height to extrude the wall, extrusion is vertical</param>
        /// <returns></returns>
        static private IfcWallStandardCase CreateWall(IfcStore model, double hauteur, double largeur, double longeur)
        {
            //
            //begin a transaction
            using (var txn = model.BeginTransaction("Create Wall"))
            {
                var wall = model.Instances.New<IfcWallStandardCase>();
                wall.Name = "A Standard rectangular wall";

                //represent wall as a rectangular profile
                var rectProf = model.Instances.New<IfcRectangleProfileDef>();
                rectProf.ProfileType = IfcProfileTypeEnum.AREA;
                rectProf.XDim = largeur;
                rectProf.YDim = hauteur;

                var insertPoint = model.Instances.New<IfcCartesianPoint>();
                insertPoint.SetXY(0, 400); //insert at arbitrary position
                rectProf.Position = model.Instances.New<IfcAxis2Placement2D>();
                rectProf.Position.Location = insertPoint;

                //model as a swept area solid
                var body = model.Instances.New<IfcExtrudedAreaSolid>();
                body.Depth = longeur;
                body.SweptArea = rectProf;
                body.ExtrudedDirection = model.Instances.New<IfcDirection>();
                body.ExtrudedDirection.SetXYZ(0, 0, 1);

                //parameters to insert the geometry in the model
                var origin = model.Instances.New<IfcCartesianPoint>();
                origin.SetXYZ(0, 0, 0);
                body.Position = model.Instances.New<IfcAxis2Placement3D>();
                body.Position.Location = origin;

                //Create a Definition shape to hold the geometry
                var shape = model.Instances.New<IfcShapeRepresentation>();
                var modelContext = model.Instances.OfType<IfcGeometricRepresentationContext>().FirstOrDefault();
                shape.ContextOfItems = modelContext;
                shape.RepresentationType = "SweptSolid";
                shape.RepresentationIdentifier = "Body";
                shape.Items.Add(body);

                //Create a Product Definition and add the model geometry to the wall
                var rep = model.Instances.New<IfcProductDefinitionShape>();
                rep.Representations.Add(shape);
                wall.Representation = rep;

                //now place the wall into the model
                var lp = model.Instances.New<IfcLocalPlacement>();
                var ax3D = model.Instances.New<IfcAxis2Placement3D>();
                ax3D.Location = origin;
                ax3D.RefDirection = model.Instances.New<IfcDirection>();
                ax3D.RefDirection.SetXYZ(0, 1, 0);
                ax3D.Axis = model.Instances.New<IfcDirection>();
                ax3D.Axis.SetXYZ(0, 0, 1);
                lp.RelativePlacement = ax3D;
                wall.ObjectPlacement = lp;


                // Where Clause: The IfcWallStandard relies on the provision of an IfcMaterialLayerSetUsage 
                var ifcMaterialLayerSetUsage = model.Instances.New<IfcMaterialLayerSetUsage>();
                var ifcMaterialLayerSet = model.Instances.New<IfcMaterialLayerSet>();
                var ifcMaterialLayer = model.Instances.New<IfcMaterialLayer>();
                ifcMaterialLayer.LayerThickness = 10;
                ifcMaterialLayerSet.MaterialLayers.Add(ifcMaterialLayer);
                ifcMaterialLayerSetUsage.ForLayerSet = ifcMaterialLayerSet;
                ifcMaterialLayerSetUsage.LayerSetDirection = IfcLayerSetDirectionEnum.AXIS2;
                ifcMaterialLayerSetUsage.DirectionSense = IfcDirectionSenseEnum.NEGATIVE;
                ifcMaterialLayerSetUsage.OffsetFromReferenceLine = 150;

                // Add material to wall
                var material = model.Instances.New<IfcMaterial>();
                material.Name = "some material";
                var ifcRelAssociatesMaterial = model.Instances.New<IfcRelAssociatesMaterial>();
                ifcRelAssociatesMaterial.RelatingMaterial = material;
                ifcRelAssociatesMaterial.RelatedObjects.Add(wall);

                ifcRelAssociatesMaterial.RelatingMaterial = ifcMaterialLayerSetUsage;

                // IfcPresentationLayerAssignment is required for CAD presentation in IfcWall or IfcWallStandardCase
                var ifcPresentationLayerAssignment = model.Instances.New<IfcPresentationLayerAssignment>();
                ifcPresentationLayerAssignment.Name = "some ifcPresentationLayerAssignment";
                ifcPresentationLayerAssignment.AssignedItems.Add(shape);


                // linear segment as IfcPolyline with two points is required for IfcWall
                var ifcPolyline = model.Instances.New<IfcPolyline>();
                var startPoint = model.Instances.New<IfcCartesianPoint>();
                startPoint.SetXY(0, 0);
                var endPoint = model.Instances.New<IfcCartesianPoint>();
                endPoint.SetXY(4000, 0);
                ifcPolyline.Points.Add(startPoint);
                ifcPolyline.Points.Add(endPoint);

                var shape2D = model.Instances.New<IfcShapeRepresentation>();
                shape2D.ContextOfItems = modelContext;
                shape2D.RepresentationIdentifier = "Axis";
                shape2D.RepresentationType = "Curve2D";
                shape2D.Items.Add(ifcPolyline);
                rep.Representations.Add(shape2D);
                txn.Commit();
                return wall;
            }

        }
        static private IfcBeam CreateBeam(IfcStore model, double width, double height, double x1, double y1, double z1, double x2, double y2, double z2, Element poutre)
        {
            using (var txn = model.BeginTransaction("Create Beam"))
            {
                var beam = model.Instances.New<IfcBeam>();
                beam.Name = poutre.Name;
                beam.GlobalId = poutre.ElementId.ToString();
                poutre.BimId = beam.EntityLabel;

                // Define the profile of the beam as a rectangle
                var rectProfile = model.Instances.New<IfcRectangleProfileDef>();
                rectProfile.ProfileType = IfcProfileTypeEnum.AREA;
                rectProfile.XDim = width;
                rectProfile.YDim = height;

                var insertPoint = model.Instances.New<IfcCartesianPoint>();
                insertPoint.SetXY(0, 0); //insert at arbitrary position
                rectProfile.Position = model.Instances.New<IfcAxis2Placement2D>();
                rectProfile.Position.Location = insertPoint;

                // Create an extrusion direction vector
                double magnitude = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));
                var direction = model.Instances.New<IfcDirection>();
                direction.SetXYZ((x2 - x1) / magnitude, (y2 - y1) / magnitude, (z2 - z1) / magnitude);

                // Create a swept area solid using the profile and extrusion direction
                var body = model.Instances.New<IfcExtrudedAreaSolid>();
                body.Depth = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));
                body.SweptArea = rectProfile;
                body.ExtrudedDirection = model.Instances.New<IfcDirection>();
                body.ExtrudedDirection.SetXYZ(0, 0, 1);

                // Set the placement of the extrusion
                var origin = model.Instances.New<IfcCartesianPoint>();
                origin.SetXYZ(0, 0, 0);
                body.Position = model.Instances.New<IfcAxis2Placement3D>();
                body.Position.Location = origin;

                // Create a definition shape to hold the geometry
                var shape = model.Instances.New<IfcShapeRepresentation>();
                var modelContext = model.Instances.OfType<IfcGeometricRepresentationContext>().FirstOrDefault();
                shape.ContextOfItems = modelContext;
                shape.RepresentationType = "SweptSolid";
                shape.RepresentationIdentifier = "Body";
                shape.Items.Add(body);

                // Create a Product Definition and add the model geometry to the beam
                var rep = model.Instances.New<IfcProductDefinitionShape>();
                rep.Representations.Add(shape);
                beam.Representation = rep;

                // Place the beam into the model
                var torigin = model.Instances.New<IfcCartesianPoint>();
                torigin.SetXYZ(x1, y1, z1);
                var lp = model.Instances.New<IfcLocalPlacement>();
                var ax3D = model.Instances.New<IfcAxis2Placement3D>();
                ax3D.Location = torigin;
                ax3D.RefDirection = model.Instances.New<IfcDirection>();
                ax3D.RefDirection.SetXYZ(0, 1, 0);
                lp.RelativePlacement = ax3D;
                ax3D.Axis = model.Instances.New<IfcDirection>();
                ax3D.Axis = direction;
                beam.ObjectPlacement = lp;

                // Define and assign material
                var material = model.Instances.New<IfcMaterial>();
                material.Name = "materialName";
                var materialLayer = model.Instances.New<IfcMaterialLayer>();
                materialLayer.Material = material;
                materialLayer.LayerThickness = 10; // Specify a suitable thickness

                var materialLayerSet = model.Instances.New<IfcMaterialLayerSet>();
                materialLayerSet.MaterialLayers.Add(materialLayer);

                var materialLayerSetUsage = model.Instances.New<IfcMaterialLayerSetUsage>();
                materialLayerSetUsage.ForLayerSet = materialLayerSet;
                materialLayerSetUsage.LayerSetDirection = IfcLayerSetDirectionEnum.AXIS2;
                materialLayerSetUsage.DirectionSense = IfcDirectionSenseEnum.NEGATIVE;
                materialLayerSetUsage.OffsetFromReferenceLine = 0; // Adjust as necessary

                var relAssociatesMaterial = model.Instances.New<IfcRelAssociatesMaterial>();
                relAssociatesMaterial.RelatingMaterial = materialLayerSetUsage;
                relAssociatesMaterial.RelatedObjects.Add(beam);

                // Define color for the beam
                (double, double, double, double) color; 
                if (poutre.Name.ToLower().Contains("mgrid"))
                {
                    color = (0, 1, 0, 0);
                }
                else if (poutre.Name.ToLower().Contains("palonnier"))
                {
                    color = (1, 1, 0, 0);
                }
                else if (poutre.Parent.Name.ToLower().Contains("câble"))
                {
                    color = (1, 0, 0, 0);
                }
                else
                {
                    color = (0.5, 0.5, 0.5, 0);
                }


                var surfaceColor = model.Instances.New<IfcSurfaceStyleRendering>();
                surfaceColor.SurfaceColour = model.Instances.New<IfcColourRgb>();
                surfaceColor.SurfaceColour.Red = color.Item1;  // Example values for grey color
                surfaceColor.SurfaceColour.Green = color.Item2;
                surfaceColor.SurfaceColour.Blue = color.Item3;
                surfaceColor.Transparency = color.Item4;  // Opaque
                surfaceColor.ReflectanceMethod = IfcReflectanceMethodEnum.NOTDEFINED;

                var surfaceStyle = model.Instances.New<IfcSurfaceStyle>();
                surfaceStyle.Styles.Add(surfaceColor);
                surfaceStyle.Side = IfcSurfaceSide.BOTH;

                var styleAssignment = model.Instances.New<IfcPresentationStyleAssignment>();
                styleAssignment.Styles.Add(surfaceStyle);

                var styledItem = model.Instances.New<IfcStyledItem>();
                styledItem.Item = body;  // body is an IfcExtrudedAreaSolid, which is a type of IfcRepresentationItem
                styledItem.Styles.Add(styleAssignment);


                // Commit transaction and return the created beam
                txn.Commit();
                return beam;
            }
        }




    }
}
