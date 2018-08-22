using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.IO;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Windows.Media.Media3D;
using OxyPlot;
using OxyPlot.Wpf;
using OxyPlot.Series;
using OxyPlot.Axes;


namespace VMS.TPS
{
    public class Script
    {
        public void Execute(ScriptContext scriptContext, Window mainWindow)
        {
            Run(scriptContext.CurrentUser,
                scriptContext.Patient,
                scriptContext.Image,
                scriptContext.StructureSet,
                scriptContext.PlanSetup,
                scriptContext.PlansInScope,
                scriptContext.PlanSumsInScope,
                mainWindow);
        }

        public void Run(
            User user,
            Patient patient,
            Image image,
            StructureSet structureSet,
            PlanSetup planSetup,
            IEnumerable<PlanSetup> planSetupsInScope,
            IEnumerable<PlanSum> planSumsInScope,
            Window mainWindow)
        {
            mainWindow.Title = "Collision Points";
            mainWindow.Content = CreatePlotView(planSetup);
        }

        private PlotView CreatePlotView(PlanSetup planSetup)
        {
            return new PlotView { Model = CreatePlotModel(planSetup) };
        }

        private PlotModel CreatePlotModel(PlanSetup planSetup)
        {
            var model = new PlotModel();
            AddAxes(model);
            TestStructures(model, planSetup);
            CalcPlannedAngles(model, planSetup);
            model.LegendPlacement = LegendPlacement.Outside;
            model.LegendPosition = LegendPosition.RightTop;
            return model;
        }

        private void TestStructures(PlotModel model, PlanSetup planSetup)
        {
            // error checking
            if (planSetup == null)
            {
                MessageBox.Show("This script requires a plan");
                return;
            }
            if (planSetup.StructureSet == null)
            {
                MessageBox.Show("This script requires a structure set");
                return;
            }

            //Checks for collisions with Body, VisionRT head adjuster structure, named VRT, and CouchSurface, if present
            var Body = planSetup.StructureSet.Structures.Where(s => s.Id == "BODY");  
            if (Body.Any())
            {
                Structure Bodystruct = Body.Single();
                if (Bodystruct.IsEmpty != true)
                {
                    CalcCollisionPoints(model, planSetup, Bodystruct);
                }
            }

            var VRT = planSetup.StructureSet.Structures.Where(s => s.Id == "VRT");   
            if (VRT.Any())
            {
                Structure VRTstruct = VRT.Single();
                if (VRTstruct.IsEmpty != true)
                {
                    CalcCollisionPoints(model, planSetup, VRTstruct);
                }
            }

            var Couch = planSetup.StructureSet.Structures.Where(s => s.Id == "CouchSurface");   
            if (Couch.Any())
            {
                Structure Couchstruct = Couch.Single();
                if (Couchstruct.IsEmpty != true)
                {
                    CalcCollisionPoints(model, planSetup, Couchstruct);
                }
            }                       
        }

        private void CalcCollisionPoints(PlotModel model, PlanSetup planSetup, Structure teststructure)
        {
            Point3DCollection testarray = teststructure.MeshGeometry.Positions;

            var isocoord = planSetup.Beams.First().IsocenterPosition;
            double IsoX = isocoord.x;
            double IsoY = isocoord.y;
            double IsoZ = isocoord.z;

            int structarrayLength = testarray.Count;
            List<double> CollisionCoordX = new List<double>();
            List<double> CollisionCoordY = new List<double>();
            List<double> CollisionCoordZ = new List<double>();
            int i = 0;
            while (i < structarrayLength)
            {
                double tempX = testarray[i].X;
                double tempY = testarray[i].Y;
                double tempZ = testarray[i].Z;

                if (tempZ >= IsoZ)  //only check collisions superior to isocenter.  not checking at couch angles <270 and >90
                {
                    double Distance = Math.Sqrt(Math.Pow(tempX - IsoX, 2) + Math.Pow(tempY - IsoY, 2) + Math.Pow(tempZ - IsoZ, 2));
                    if (Distance >= 230)   // 230mm provides safety zone to bottom of SRS cone at 250mm distance from iso.  Code does not account for diameter of cone (7 cm)
                    {
                        CollisionCoordX.Add(tempX);
                        CollisionCoordY.Add(tempY);
                        CollisionCoordZ.Add(tempZ);
                    }
                }
                i++;
            }
            
            List<double> CollisionCoordCouchAng = new List<double>();
            List<double> CollisionCoordGantryAng = new List<double>();
            int CoordLength = CollisionCoordX.Count;
            int ii = 0;
            while (ii < CoordLength)
            {
                double rho = (Math.Atan2(CollisionCoordZ[ii], CollisionCoordX[ii]));  //in X-Z plane (Y=0), angle rho from X-axis used to get couch angle
                double couchang = 360 - rho * (180 / Math.PI);

                if (couchang <= 270)
                {
                    couchang = couchang - 180;
                }
                if (couchang >= 359.9)  //only doing this for display purposes
                {
                    couchang = couchang - 360;
                }
                CollisionCoordCouchAng.Add(couchang);

                //Dicom Z + to superior, Y + to posterior, X + to Left (HFS)
                //rotation transformation around Y axis by angle pi to transform into plane of gantry arc. 
                double xprime = CollisionCoordX[ii] * Math.Cos(rho) + CollisionCoordZ[ii] * Math.Sin(rho);
                double yprime = CollisionCoordY[ii];

                double phi = Math.Atan2(yprime, xprime) * (180 / Math.PI);   //atan2 returns angle from x' axis.   

                double gantryang = phi + 90;   //convert from spherical-polar(dicom) to IEC61217
                if (gantryang <= 0)
                {
                    gantryang = gantryang + 360;
                }
                if (couchang <= 90)
                {
                    gantryang = 360 - gantryang;
                }
                if (gantryang >= 359.9)  //only doing this for display purposes
                {
                    gantryang = gantryang - 360;
                }

                CollisionCoordGantryAng.Add(gantryang);

                ii++;
            }

            int collpointsize = 4;
            var collisionSeries = CreateSeries(CollisionCoordCouchAng, CollisionCoordGantryAng, collpointsize);
            collisionSeries.Title = teststructure.Id; 
            model.Series.Add(collisionSeries);          
        }


        private void CalcPlannedAngles(PlotModel model, PlanSetup planSetup)
        {
            List<double> PlannedCouchAng = new List<double>();
            List<double> PlannedGantryAng = new List<double>();

            int numberofbeams = planSetup.Beams.Count();
            if (numberofbeams == 0)
            {
                return;
            }

            int n = 0;
            while (n < numberofbeams)
            {
                var beam = planSetup.Beams.ElementAt(n);

                if (beam.IsSetupField == false)
                {
                    String beamtype = beam.Technique.Id;
                    if (beamtype == "SRS ARC")   // only plot arcs
                    {
                        double CouchAng = beam.ControlPoints.First().PatientSupportAngle;
                        double GantryStartAng = beam.ControlPoints.First().GantryAngle;
                        double GantryStopAng = beam.ControlPoints.Last().GantryAngle;
                        string GantryRotationDir = beam.GantryDirection.ToString();

                        if (GantryRotationDir == "Clockwise")
                        {
                            int nn = Convert.ToInt32(GantryStartAng);
                            while (nn <= GantryStopAng)
                            {
                                PlannedCouchAng.Add(CouchAng);
                                PlannedGantryAng.Add(nn);
                                nn++;
                            }
                        }
                        else if (GantryRotationDir == "CounterClockwise")
                        {
                            int nn = Convert.ToInt32(GantryStartAng);
                            while (nn >= GantryStopAng)
                            {
                                PlannedCouchAng.Add(CouchAng);
                                PlannedGantryAng.Add(nn);
                                nn--;
                            }
                        }
                    }
                }
    
                n++;
            }
            int planpointsize = 2;
            var plannedSeries = CreateSeries(PlannedCouchAng, PlannedGantryAng, planpointsize);

            plannedSeries.Title = "Planned Arcs";
            model.Series.Add(plannedSeries);
        }


        private OxyPlot.Series.ScatterSeries CreateSeries(List<double> CouchAng, List<double> GantryAng, int size)
        {
            var scatterSeries = new OxyPlot.Series.ScatterSeries();
            
            int CoordLength = CouchAng.Count;
            for (int iii= 0;  iii < CoordLength; iii++)
            {
                var point = new ScatterPoint(CouchAng[iii], GantryAng[iii], size);
                scatterSeries.Points.Add(point);
            }
            return scatterSeries;
        }

        
        private static void AddAxes(PlotModel model)
        {
            // Add x- axis
            model.Axes.Add(new OxyPlot.Axes.LinearAxis
            {
                Title = "Couch Angle",
                Position = AxisPosition.Bottom,
                Minimum = -1,
                Maximum = 360,
            });
            // Add y- axis
            model.Axes.Add(new OxyPlot.Axes.LinearAxis
            {
                Title = "Gantry Angle",
                Position = AxisPosition.Left,
                Minimum = -1,
                Maximum = 360,
            });
        }
        
    }
}
