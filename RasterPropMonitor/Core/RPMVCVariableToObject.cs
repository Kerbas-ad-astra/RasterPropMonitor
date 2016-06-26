﻿/*****************************************************************************
 * RasterPropMonitor
 * =================
 * Plugin for Kerbal Space Program
 *
 *  by Mihara (Eugene Medvedev), MOARdV, and other contributors
 * 
 * RasterPropMonitor is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, revision
 * date 29 June 2007, or (at your option) any later version.
 * 
 * RasterPropMonitor is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with RasterPropMonitor.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************/
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace JSI
{
    public partial class RPMVesselComputer : VesselModule
    {
        private const float KelvinToCelsius = -273.15f;
        internal const float MetersToFeet = 3.2808399f;
        internal const float MetersPerSecondToKnots = 1.94384449f;
        internal const float MetersPerSecondToFeetPerMinute = 196.850394f;

        //--- The guts of the variable processor
        #region VariableToObject
        internal VariableEvaluator GetEvaluator(string input, out bool cacheable)
        {
            cacheable = true;

            if (input.IndexOf("_", StringComparison.Ordinal) > -1)
            {
                string[] tokens = input.Split('_');

                if (tokens.Length == 2 && tokens[0] == "SYSR")
                {
                    foreach (KeyValuePair<string, string> resourceType in RPMGlobals.systemNamedResources)
                    {
                        if (tokens[1].StartsWith(resourceType.Key, StringComparison.Ordinal))
                        {
                            return (string variable, RasterPropMonitorComputer rpmComp) => { return resources.ListElement(variable); };
                        }
                    }
                    return (string variable, RasterPropMonitorComputer rpmComp) => { return variable; };
                }

                // If input starts with "LISTR" we're handling it specially -- it's a list of all resources.
                // The variables are named like LISTR_<number>_<NAME|VAL|MAX>
                if (tokens.Length == 3 && tokens[0] == "LISTR")
                {
                    return (string variable, RasterPropMonitorComputer rpmComp) =>
                    {
                        string[] toks = variable.Split('_');
                        ushort resourceID = Convert.ToUInt16(toks[1]);
                        string resourceName = resources.GetActiveResourceByIndex(resourceID);
                        if (toks[2] == "NAME")
                        {
                            return resourceName;
                        }
                        if (string.IsNullOrEmpty(resourceName))
                        {
                            return 0d;
                        }
                        else
                        {
                            return toks[2].StartsWith("STAGE", StringComparison.Ordinal) ?
                                resources.ListElement(resourceName, toks[2].Substring("STAGE".Length), true) :
                                resources.ListElement(resourceName, toks[2], false);
                        }
                    };
                }

                // We do similar things for crew rosters.
                // The syntax is therefore CREW_<index>_<FIRST|LAST|FULL>
                // Part-local crew list is identical but CREWLOCAL_.
                if (tokens.Length == 3 && (tokens[0] == "CREW" || tokens[0] == "CREWLOCAL"))
                {
                    return (string variable, RasterPropMonitorComputer rpmComp) =>
                    {
                        string[] toks = variable.Split('_');
                        ushort crewSeatID = Convert.ToUInt16(toks[1]);
                        switch (toks[0])
                        {
                            case "CREW":
                                return CrewListElement(toks[2], crewSeatID, vesselCrew, vesselCrewMedical);
                            case "CREWLOCAL":
                                return CrewListElement(toks[2], crewSeatID, localCrew, localCrewMedical);
                        }
                        return variable;
                    };
                }
            }

            //switch (input)
            //{
                // Speeds.
                //case "VERTSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return speedVertical; };
                //case "VERTSPEEDLOG10":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return JUtil.PseudoLog10(speedVertical); };
                //case "VERTSPEEDROUNDED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return speedVerticalRounded; };
                //case "RADARALTVERTSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return radarAltitudeRate; };
                //case "TERMINALVELOCITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return TerminalVelocity(); };
                //case "SURFSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.srfSpeed; };
                //case "SURFSPEEDMACH":
                //    // Mach number wiggles around 1e-7 when sitting in launch
                //    // clamps before launch, so pull it down to zero if it's close.
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (vessel.mach < 0.001) ? 0.0 : vessel.mach; };
                //case "ORBTSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.GetVel().magnitude; };
                //case "TRGTSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return velocityRelativeTarget.magnitude; };
                //case "HORZVELOCITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return speedHorizontal; };
                //case "HORZVELOCITYFORWARD":
                //    // Negate it, since this is actually movement on the Z axis,
                //    // and we want to treat it as a 2D projection on the surface
                //    // such that moving "forward" has a positive value.
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return -Vector3d.Dot(vessel.srf_velocity, surfaceForward); };
                //case "HORZVELOCITYRIGHT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3d.Dot(vessel.srf_velocity, surfaceRight); };
                //case "EASPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        double densityRatio = (AeroExtensions.GetCurrentDensity(vessel) / 1.225);
                //        return vessel.srfSpeed * Math.Sqrt(densityRatio);
                //    };
                //case "IASPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        double densityRatio = (AeroExtensions.GetCurrentDensity(vessel) / 1.225);
                //        double pressureRatio = AeroExtensions.StagnationPressureCalc(vessel.mainBody, vessel.mach);
                //        return vessel.srfSpeed * Math.Sqrt(densityRatio) * pressureRatio;
                //    };
                //case "APPROACHSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return approachSpeed; };
                //case "SELECTEDSPEED":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        switch (FlightGlobals.speedDisplayMode)
                //        {
                //            case FlightGlobals.SpeedDisplayModes.Orbit:
                //                return vessel.orbit.GetVel().magnitude;
                //            case FlightGlobals.SpeedDisplayModes.Surface:
                //                return vessel.srfSpeed;
                //            case FlightGlobals.SpeedDisplayModes.Target:
                //                return velocityRelativeTarget.magnitude;
                //        }
                //        return double.NaN;
                //    };

                //case "TGTRELX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            return Vector3d.Dot(FlightGlobals.ship_tgtVelocity, FlightGlobals.ActiveVessel.ReferenceTransform.right);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };

                //case "TGTRELY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            return Vector3d.Dot(FlightGlobals.ship_tgtVelocity, FlightGlobals.ActiveVessel.ReferenceTransform.forward);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "TGTRELZ":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            return Vector3d.Dot(FlightGlobals.ship_tgtVelocity, FlightGlobals.ActiveVessel.ReferenceTransform.up);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };

                //case "TIMETOIMPACTSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return TimeToImpact(); };
                //case "SPEEDATIMPACT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return SpeedAtImpact(totalCurrentThrust); };
                //case "BESTSPEEDATIMPACT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return SpeedAtImpact(totalLimitedMaximumThrust); };
                //case "SUICIDEBURNSTARTSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (vessel.orbit.PeA > 0.0)
                //        {
                //            return double.NaN;
                //        }
                //        return SuicideBurnCountdown();
                //    };

                //case "LATERALBRAKEDISTANCE":
                //    // (-(SHIP:SURFACESPEED)^2)/(2*(ship:maxthrust/ship:mass)) 
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (totalLimitedMaximumThrust <= 0.0)
                //        {
                //            // It should be impossible for wet mass to be zero.
                //            return -1.0;
                //        }
                //        return (speedHorizontal * speedHorizontal) / (2.0 * totalLimitedMaximumThrust / totalShipWetMass);
                //    };

                //// Altitudes
                //case "ALTITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return altitudeASL; };
                //case "ALTITUDELOG10":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return JUtil.PseudoLog10(altitudeASL); };
                //case "RADARALT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return altitudeTrue; };
                //case "RADARALTLOG10":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return JUtil.PseudoLog10(altitudeTrue); };
                //case "RADARALTOCEAN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (vessel.mainBody.ocean)
                //        {
                //            return Math.Min(altitudeASL, altitudeTrue);
                //        }
                //        return altitudeTrue;
                //    };
                //case "RADARALTOCEANLOG10":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (vessel.mainBody.ocean)
                //        {
                //            return JUtil.PseudoLog10(Math.Min(altitudeASL, altitudeTrue));
                //        }
                //        return JUtil.PseudoLog10(altitudeTrue);
                //    };
                //case "ALTITUDEBOTTOM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return altitudeBottom; };
                //case "ALTITUDEBOTTOMLOG10":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return JUtil.PseudoLog10(altitudeBottom); };
                //case "TERRAINHEIGHT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.terrainAltitude; };
                //case "TERRAINDELTA":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return terrainDelta; };
                //case "TERRAINHEIGHTLOG10":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return JUtil.PseudoLog10(vessel.terrainAltitude); };
                //case "DISTTOATMOSPHERETOP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.atmosphereDepth - altitudeASL; };

                //// Atmospheric values
                //case "ATMPRESSURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.staticPressurekPa * PhysicsGlobals.KpaToAtmospheres; };
                //case "ATMDENSITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.atmDensity; };
                ////case "DYNAMICPRESSURE":
                ////    return DynamicPressure();
                //case "ATMOSPHEREDEPTH":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (vessel.mainBody.atmosphere)
                //        {
                //            float depth;
                //            try
                //            {
                //                depth = linearAtmosGauge.gauge.Value.Clamp(0.0f, 1.0f);
                //            }
                //            catch
                //            {
                //                depth = (float)((upperAtmosphereLimit + Math.Log(FlightGlobals.getAtmDensity(vessel.staticPressurekPa * PhysicsGlobals.KpaToAtmospheres, FlightGlobals.Bodies[1].atmosphereTemperatureSeaLevel) /
                //                FlightGlobals.getAtmDensity(FlightGlobals.currentMainBody.atmospherePressureSeaLevel, FlightGlobals.currentMainBody.atmosphereTemperatureSeaLevel))) / upperAtmosphereLimit).Clamp(0.0f, 1.0f);
                //            }

                //            return depth;
                //        }
                //        else
                //        {
                //            return 0.0f;
                //        }
                //    };

                //// Masses.
                //case "MASSDRY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalShipDryMass; };
                //case "MASSWET":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalShipWetMass; };
                //case "MASSRESOURCES":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalShipWetMass - totalShipDryMass; };
                //case "MASSPROPELLANT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return resources.PropellantMass(false); };
                //case "MASSPROPELLANTSTAGE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return resources.PropellantMass(true); };

                // The delta V calculation.
                //case "DELTAV":
                //    return DeltaV();
                //case "DELTAVSTAGE":
                //    return DeltaVStage();

                //// Thrust and related
                //case "THRUST":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalCurrentThrust; };
                //case "THRUSTMAX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalLimitedMaximumThrust; };
                //case "THRUSTMAXRAW":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalRawMaximumThrust; };
                //case "THRUSTLIMIT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalRawMaximumThrust > 0.0f) ? totalLimitedMaximumThrust / totalRawMaximumThrust : 0.0f; };
                //case "TWR":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalCurrentThrust / (totalShipWetMass * localGeeASL)); };
                //case "TWRMAX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalLimitedMaximumThrust / (totalShipWetMass * localGeeASL)); };
                //case "ACCEL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalCurrentThrust / totalShipWetMass); };
                //case "MAXACCEL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalLimitedMaximumThrust / totalShipWetMass); };
                //case "GFORCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.geeForce_immediate; };
                //case "EFFECTIVEACCEL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.acceleration.magnitude; };
                //case "REALISP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return actualAverageIsp; };
                //case "MAXISP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return actualMaxIsp; };
                //case "CURRENTENGINEFUELFLOW":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return currentEngineFuelFlow; };
                //case "MAXENGINEFUELFLOW":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return maxEngineFuelFlow; };
                //case "HOVERPOINT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (localGeeDirect / (totalLimitedMaximumThrust / totalShipWetMass)).Clamp(0.0f, 1.0f); };
                //case "HOVERPOINTEXISTS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return ((localGeeDirect / (totalLimitedMaximumThrust / totalShipWetMass)) > 1.0f) ? -1.0 : 1.0; };
                //case "EFFECTIVERAWTHROTTLE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalRawMaximumThrust > 0.0f) ? (totalCurrentThrust / totalRawMaximumThrust) : 0.0f; };
                //case "EFFECTIVETHROTTLE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (totalLimitedMaximumThrust > 0.0f) ? (totalCurrentThrust / totalLimitedMaximumThrust) : 0.0f; };
                //case "DRAG":
                //    return DragForce();
                //case "DRAGACCEL":
                //    return DragAccel();
                //case "LIFT":
                //    return LiftForce();
                //case "LIFTACCEL":
                //    return LiftAccel();
                //case "ACCELPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, prograde); };
                //case "ACCELRADIAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, radialOut); };
                //case "ACCELNORMAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, normalPlus); };
                //case "ACCELSURFPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, vessel.srf_velocity.normalized); };
                //case "ACCELFORWARD":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, forward); };
                //case "ACCELRIGHT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, right); };
                //case "ACCELTOP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3.Dot(vessel.acceleration, top); };

                //// Maneuvers
                //case "MNODETIMESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            return -(node.UT - Planetarium.GetUniversalTime());
                //        }
                //        return double.NaN;
                //    };
                //case "MNODEDV":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            return node.GetBurnVector(vessel.orbit).magnitude;
                //        }
                //        return 0d;
                //    };
                //case "MNODEBURNTIMESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null && totalLimitedMaximumThrust > 0 && actualAverageIsp > 0.0f)
                //        {
                //            return actualAverageIsp * (1.0f - Math.Exp(-node.GetBurnVector(vessel.orbit).magnitude / actualAverageIsp / gee)) / (totalLimitedMaximumThrust / (totalShipWetMass * gee));
                //        }
                //        return double.NaN;
                //    };
                //case "MNODEEXISTS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return node == null ? -1d : 1d; };

                //case "MNODEDVPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            Vector3d burnVector = node.GetBurnVector(vessel.orbit);
                //            return Vector3d.Dot(burnVector, vessel.orbit.Prograde(node.UT));
                //        }
                //        return 0.0;
                //    };
                //case "MNODEDVNORMAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            Vector3d burnVector = node.GetBurnVector(vessel.orbit);
                //            // NormalPlus seems to be backwards...
                //            return -Vector3d.Dot(burnVector, vessel.orbit.NormalPlus(node.UT));
                //        }
                //        return 0.0;
                //    };
                //case "MNODEDVRADIAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            Vector3d burnVector = node.GetBurnVector(vessel.orbit);
                //            return Vector3d.Dot(burnVector, vessel.orbit.RadialPlus(node.UT));
                //        }
                //        return 0.0;
                //    };

                //case "MNODEPERIAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //        {
                //            if (node != null && node.nextPatch != null)
                //            {
                //                return node.nextPatch.PeA;
                //            }
                //            return double.NaN;
                //        };
                //case "MNODEAPOAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null && node.nextPatch != null)
                //        {
                //            return node.nextPatch.ApA;
                //        }
                //        return double.NaN;
                //    };
                //case "MNODEINCLINATION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null && node.nextPatch != null)
                //        {
                //            return node.nextPatch.inclination;
                //        }
                //        return double.NaN;
                //    };
                //case "MNODEECCENTRICITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null && node.nextPatch != null)
                //        {
                //            return node.nextPatch.eccentricity;
                //        }
                //        return double.NaN;
                //    };

                //case "MNODETARGETCLOSESTAPPROACHTIME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null || node == null || node.nextPatch == null)
                //        {
                //            return double.NaN;
                //        }
                //        else
                //        {
                //            double approachTime, approachDistance;
                //            approachDistance = JUtil.GetClosestApproach(node.nextPatch, target, out approachTime);
                //            return approachTime - Planetarium.GetUniversalTime();
                //        }
                //    };
                //case "MNODETARGETCLOSESTAPPROACHDISTANCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null || node == null || node.nextPatch == null)
                //        {
                //            return double.NaN;
                //        }
                //        else
                //        {
                //            double approachTime;
                //            return JUtil.GetClosestApproach(node.nextPatch, target, out approachTime);
                //        }
                //    };
                //case "MNODERELATIVEINCLINATION":
                //    // MechJeb's targetables don't have orbits.
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null || node == null || node.nextPatch == null)
                //        {
                //            return double.NaN;
                //        }
                //        else
                //        {
                //            return targetOrbit.referenceBody != node.nextPatch.referenceBody ?
                //                -1d :
                //                Math.Abs(Vector3d.Angle(node.nextPatch.SwappedOrbitNormal(), targetOrbit.SwappedOrbitNormal()));
                //        }
                //    };

                //// Orbital parameters
                //case "ORBITBODY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.name; };
                //case "PERIAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return vessel.orbit.PeA;
                //        return double.NaN;
                //    };
                //case "APOAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            return vessel.orbit.ApA;
                //        }
                //        return double.NaN;
                //    };
                //case "INCLINATION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            return vessel.orbit.inclination;
                //        }
                //        return double.NaN;
                //    };
                //case "ECCENTRICITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            return vessel.orbit.eccentricity;
                //        }
                //        return double.NaN;
                //    };
                //case "SEMIMAJORAXIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            return vessel.orbit.semiMajorAxis;
                //        }
                //        return double.NaN;
                //    };

                //case "ORBPERIODSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return vessel.orbit.period;
                //        return double.NaN;
                //    };
                //case "TIMETOAPSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return vessel.orbit.timeToAp;
                //        return double.NaN;
                //    };
                //case "TIMETOPESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return vessel.orbit.eccentricity < 1 ?
                //                vessel.orbit.timeToPe :
                //                -vessel.orbit.meanAnomaly / (2 * Math.PI / vessel.orbit.period);
                //        return double.NaN;
                //    };
                //case "TIMESINCELASTAP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return vessel.orbit.period - vessel.orbit.timeToAp;
                //        return double.NaN;
                //    };
                //case "TIMESINCELASTPE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return vessel.orbit.period - (vessel.orbit.eccentricity < 1 ? vessel.orbit.timeToPe : -vessel.orbit.meanAnomaly / (2 * Math.PI / vessel.orbit.period));
                //        return double.NaN;
                //    };
                //case "TIMETONEXTAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            double apsisType = NextApsisType();
                //            if (apsisType < 0.0)
                //            {
                //                return vessel.orbit.eccentricity < 1 ?
                //                    vessel.orbit.timeToPe :
                //                    -vessel.orbit.meanAnomaly / (2 * Math.PI / vessel.orbit.period);
                //            }
                //            return vessel.orbit.timeToAp;
                //        }
                //        return 0.0;
                //    };
                //case "NEXTAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            double apsisType = NextApsisType();
                //            if (apsisType < 0.0)
                //            {
                //                return vessel.orbit.PeA;
                //            }
                //            if (apsisType > 0.0)
                //            {
                //                return vessel.orbit.ApA;
                //            }
                //        }
                //        return double.NaN;
                //    };
                //case "NEXTAPSISTYPE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return NextApsisType();
                //    };
                //case "ORBITMAKESSENSE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //            return 1d;
                //        return -1d;
                //    };
                //case "TIMETOANEQUATORIAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility && vessel.orbit.AscendingNodeEquatorialExists())
                //            return vessel.orbit.TimeOfAscendingNodeEquatorial(Planetarium.GetUniversalTime()) - Planetarium.GetUniversalTime();
                //        return double.NaN;
                //    };
                //case "TIMETODNEQUATORIAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility && vessel.orbit.DescendingNodeEquatorialExists())
                //            return vessel.orbit.TimeOfDescendingNodeEquatorial(Planetarium.GetUniversalTime()) - Planetarium.GetUniversalTime();
                //        return double.NaN;
                //    };
                //case "TIMETOATMOSPHERESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        double timeToAtm = 0.0;
                //        if (orbitSensibility && vessel.orbit.referenceBody.atmosphere == true)
                //        {
                //            try
                //            {
                //                double now = Planetarium.GetUniversalTime();
                //                timeToAtm = vessel.orbit.NextTimeOfRadius(now, vessel.orbit.referenceBody.atmosphereDepth + vessel.orbit.referenceBody.Radius) - now;
                //                timeToAtm = Math.Max(timeToAtm, 0.0);
                //            }
                //            catch
                //            {
                //                //...
                //            }
                //        }
                //        return timeToAtm;
                //    };

                //// SOI changes in orbits.
                //case "ENCOUNTEREXISTS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            switch (vessel.orbit.patchEndTransition)
                //            {
                //                case Orbit.PatchTransitionType.ESCAPE:
                //                    return -1d;
                //                case Orbit.PatchTransitionType.ENCOUNTER:
                //                    return 1d;
                //            }
                //        }
                //        return 0d;
                //    };
                //case "ENCOUNTERTIME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility &&
                //            (vessel.orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER ||
                //            vessel.orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE))
                //        {
                //            return vessel.orbit.UTsoi - Planetarium.GetUniversalTime();
                //        }
                //        return 0.0;
                //    };
                //case "ENCOUNTERBODY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility)
                //        {
                //            switch (vessel.orbit.patchEndTransition)
                //            {
                //                case Orbit.PatchTransitionType.ENCOUNTER:
                //                    return vessel.orbit.nextPatch.referenceBody.bodyName;
                //                case Orbit.PatchTransitionType.ESCAPE:
                //                    return vessel.mainBody.referenceBody.bodyName;
                //            }
                //        }
                //        return string.Empty;
                //    };

                //// Time
                //case "UTSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (GameSettings.KERBIN_TIME)
                //        {
                //            return Planetarium.GetUniversalTime() + 426 * 6 * 60 * 60;
                //        }
                //        return Planetarium.GetUniversalTime() + 365 * 24 * 60 * 60;
                //    };
                //case "TIMEOFDAYSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (GameSettings.KERBIN_TIME)
                //        {
                //            return Planetarium.GetUniversalTime() % (6.0 * 60.0 * 60.0);
                //        }
                //        else
                //        {
                //            return Planetarium.GetUniversalTime() % (24.0 * 60.0 * 60.0);
                //        }
                //    };
                //case "METSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.missionTime; };

                //// Names!
                //case "NAME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.vesselName; };
                //case "VESSELTYPE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.vesselType.ToString(); };
                //case "TARGETTYPE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null)
                //        {
                //            return targetVessel.vesselType.ToString();
                //        }
                //        if (targetDockingNode != null)
                //        {
                //            return "Port";
                //        }
                //        if (targetBody != null)
                //        {
                //            return "Celestial";
                //        }
                //        return "Position";
                //    };

                //// Coordinates.
                //case "LATITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.mainBody.GetLatitude(CoM); };
                //case "LONGITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return JUtil.ClampDegrees180(vessel.mainBody.GetLongitude(CoM)); };
                //case "TARGETLATITUDE":
                //case "LATITUDETGT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    { // These targetables definitely don't have any coordinates.
                //        if (target == null || target is CelestialBody)
                //        {
                //            return double.NaN;
                //        }
                //        // These definitely do.
                //        if (target is Vessel || target is ModuleDockingNode)
                //        {
                //            return target.GetVessel().mainBody.GetLatitude(target.GetTransform().position);
                //        }
                //        // We're going to take a guess here and expect MechJeb's PositionTarget and DirectionTarget,
                //        // which don't have vessel structures but do have a transform.
                //        return vessel.mainBody.GetLatitude(target.GetTransform().position);
                //    };
                //case "TARGETLONGITUDE":
                //case "LONGITUDETGT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || target is CelestialBody)
                //        {
                //            return double.NaN;
                //        }
                //        if (target is Vessel || target is ModuleDockingNode)
                //        {
                //            return JUtil.ClampDegrees180(target.GetVessel().mainBody.GetLongitude(target.GetTransform().position));
                //        }
                //        return vessel.mainBody.GetLongitude(target.GetTransform().position);
                //    };

                //// Orientation
                //case "HEADING":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return rotationVesselSurface.eulerAngles.y; };
                //case "PITCH":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rotationVesselSurface.eulerAngles.x > 180.0f) ? (360.0f - rotationVesselSurface.eulerAngles.x) : -rotationVesselSurface.eulerAngles.x; };
                //case "ROLL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rotationVesselSurface.eulerAngles.z > 180.0f) ? (360.0f - rotationVesselSurface.eulerAngles.z) : -rotationVesselSurface.eulerAngles.z; };
                //case "PITCHRATE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return -vessel.angularVelocity.x * Mathf.Rad2Deg; };
                //case "ROLLRATE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return -vessel.angularVelocity.y * Mathf.Rad2Deg; };
                //case "YAWRATE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return -vessel.angularVelocity.z * Mathf.Rad2Deg; };
                //case "ANGLEOFATTACK":
                //    return AngleOfAttack();
                //case "SIDESLIP":
                //    return SideSlip();
                // These values get odd when they're way out on the edge of the
                // navball because they're projected into two dimensions.
                //case "PITCHSURFPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(vessel.srf_velocity.normalized); };
                //case "PITCHSURFRETROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(-vessel.srf_velocity.normalized); };
                //case "PITCHPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(prograde); };
                //case "PITCHRETROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(-prograde); };
                //case "PITCHRADIALIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(-radialOut); };
                //case "PITCHRADIALOUT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(radialOut); };
                //case "PITCHNORMALPLUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(normalPlus); };
                //case "PITCHNORMALMINUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativePitch(-normalPlus); };
                //case "PITCHNODE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            return GetRelativePitch(node.GetBurnVector(vessel.orbit).normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "PITCHTARGET":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            return GetRelativePitch(-targetSeparation.normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "PITCHTARGETRELPLUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && velocityRelativeTarget.sqrMagnitude > 0.0)
                //        {
                //            return GetRelativePitch(velocityRelativeTarget.normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "PITCHTARGETRELMINUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && velocityRelativeTarget.sqrMagnitude > 0.0)
                //        {
                //            return GetRelativePitch(-velocityRelativeTarget.normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "YAWSURFPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(vessel.srf_velocity.normalized); };
                //case "YAWSURFRETROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(-vessel.srf_velocity.normalized); };
                //case "YAWPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(prograde); };
                //case "YAWRETROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(-prograde); };
                //case "YAWRADIALIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(-radialOut); };
                //case "YAWRADIALOUT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(radialOut); };
                //case "YAWNORMALPLUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(normalPlus); };
                //case "YAWNORMALMINUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GetRelativeYaw(-normalPlus); };
                //case "YAWNODE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (node != null)
                //        {
                //            return GetRelativeYaw(node.GetBurnVector(vessel.orbit).normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "YAWTARGET":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            return GetRelativeYaw(-targetSeparation.normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "YAWTARGETRELPLUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && velocityRelativeTarget.sqrMagnitude > 0.0)
                //        {
                //            return GetRelativeYaw(velocityRelativeTarget.normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "YAWTARGETRELMINUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && velocityRelativeTarget.sqrMagnitude > 0.0)
                //        {
                //            return GetRelativeYaw(-velocityRelativeTarget.normalized);
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };


                //// Targeting. Probably the most finicky bit right now.
                //case "TARGETNAME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //            return string.Empty;
                //        if (target is Vessel || target is CelestialBody || target is ModuleDockingNode)
                //            return target.GetName();
                //        // What remains is MechJeb's ITargetable implementations, which also can return a name,
                //        // but the newline they return in some cases needs to be removed.
                //        return target.GetName().Replace('\n', ' ');
                //    };
                //case "TARGETDISTANCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //            return targetDistance;
                //        return -1d;
                //    };
                //case "TARGETGROUNDDISTANCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            Vector3d targetGroundPos = target.ProjectPositionOntoSurface(vessel.mainBody);
                //            if (targetGroundPos != Vector3d.zero)
                //            {
                //                return Vector3d.Distance(targetGroundPos, vessel.ProjectPositionOntoSurface());
                //            }
                //        }
                //        return -1d;
                //    };
                //case "RELATIVEINCLINATION":
                //    // MechJeb's targetables don't have orbits.
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbit != null)
                //        {
                //            return targetOrbit.referenceBody != vessel.orbit.referenceBody ?
                //                -1d :
                //                Math.Abs(Vector3d.Angle(vessel.GetOrbit().SwappedOrbitNormal(), targetOrbit.SwappedOrbitNormal()));
                //        }
                //        return double.NaN;
                //    };
                //case "TARGETORBITBODY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbit != null)
                //            return targetOrbit.referenceBody.name;
                //        return string.Empty;
                //    };
                //case "TARGETEXISTS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //            return -1d;
                //        if (target is Vessel)
                //            return 1d;
                //        return 0d;
                //    };
                //case "TARGETISDOCKINGPORT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //            return -1d;
                //        if (target is ModuleDockingNode)
                //            return 1d;
                //        return 0d;
                //    };
                //case "TARGETISVESSELORPORT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //            return -1d;
                //        if (target is ModuleDockingNode || target is Vessel)
                //            return 1d;
                //        return 0d;
                //    };
                //case "TARGETISCELESTIAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //            return -1d;
                //        if (target is CelestialBody)
                //            return 1d;
                //        return 0d;
                //    };
                //case "TARGETISPOSITION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //        {
                //            if (target == null)
                //            {
                //                return -1d;
                //            }
                //            else if (target is PositionTarget)
                //            {
                //                return 1d;
                //            }
                //            else
                //            {
                //                return 0d;
                //            }
                //        };
                //case "TARGETSITUATION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target is Vessel)
                //            return SituationString(target.GetVessel().situation);
                //        return string.Empty;
                //    };
                //case "TARGETALTITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //        {
                //            return -1d;
                //        }
                //        if (target is CelestialBody)
                //        {
                //            if (targetBody == vessel.mainBody || targetBody == Planetarium.fetch.Sun)
                //            {
                //                return 0d;
                //            }
                //            else
                //            {
                //                return targetBody.referenceBody.GetAltitude(targetBody.position);
                //            }
                //        }
                //        if (target is Vessel || target is ModuleDockingNode)
                //        {
                //            return target.GetVessel().mainBody.GetAltitude(target.GetVessel().CoM);
                //        }
                //        else
                //        {
                //            return vessel.mainBody.GetAltitude(target.GetTransform().position);
                //        }
                //    };
                //// MOARdV: I don't think these are needed - I don't remember why we needed targetOrbit
                ////if (targetOrbit != null)
                ////{
                ////    return targetOrbit.altitude;
                ////}
                ////return -1d;
                //case "TARGETSEMIMAJORAXIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null)
                //            return double.NaN;
                //        if (targetOrbit != null)
                //            return targetOrbit.semiMajorAxis;
                //        return double.NaN;
                //    };
                //case "TIMETOANWITHTARGETSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null)
                //            return double.NaN;
                //        return vessel.GetOrbit().TimeOfAscendingNode(targetOrbit, Planetarium.GetUniversalTime()) - Planetarium.GetUniversalTime();
                //    };
                //case "TIMETODNWITHTARGETSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null)
                //            return double.NaN;
                //        return vessel.GetOrbit().TimeOfDescendingNode(targetOrbit, Planetarium.GetUniversalTime()) - Planetarium.GetUniversalTime();
                //    };
                //case "TARGETCLOSESTAPPROACHTIME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null || orbitSensibility == false)
                //        {
                //            return double.NaN;
                //        }
                //        else
                //        {
                //            double approachTime, approachDistance;
                //            approachDistance = JUtil.GetClosestApproach(vessel.GetOrbit(), target, out approachTime);
                //            return approachTime - Planetarium.GetUniversalTime();
                //        }
                //    };
                //case "TARGETCLOSESTAPPROACHDISTANCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target == null || targetOrbit == null || orbitSensibility == false)
                //        {
                //            return double.NaN;
                //        }
                //        else
                //        {
                //            double approachTime;
                //            return JUtil.GetClosestApproach(vessel.GetOrbit(), target, out approachTime);
                //        }
                //    };

                //// Space Objects (asteroid) specifics
                //case "TARGETSIGNALSTRENGTH":
                //    // MOARdV:
                //    // Based on observation, it appears the discovery
                //    // level bitfield is basically unused - either the
                //    // craft is Owned (-1) or Unowned (29 - which is the
                //    // OR of all the bits).  However, maybe career mode uses
                //    // the bits, so I will make a guess on what knowledge is
                //    // appropriate here.
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.DiscoveryInfo.Level != DiscoveryLevels.Owned && targetVessel.DiscoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.Presence))
                //        {
                //            return targetVessel.DiscoveryInfo.GetSignalStrength(targetVessel.DiscoveryInfo.lastObservedTime);
                //        }
                //        else
                //        {
                //            return -1.0;
                //        }
                //    };

                //case "TARGETSIGNALSTRENGTHCAPTION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.DiscoveryInfo.Level != DiscoveryLevels.Owned && targetVessel.DiscoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.Presence))
                //        {
                //            return DiscoveryInfo.GetSignalStrengthCaption(targetVessel.DiscoveryInfo.GetSignalStrength(targetVessel.DiscoveryInfo.lastObservedTime));
                //        }
                //        else
                //        {
                //            return "";
                //        }
                //    };

                //case "TARGETLASTOBSERVEDTIMEUT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.DiscoveryInfo.Level != DiscoveryLevels.Owned && targetVessel.DiscoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.Presence))
                //        {
                //            return targetVessel.DiscoveryInfo.lastObservedTime;
                //        }
                //        else
                //        {
                //            return -1.0;
                //        }
                //    };

                //case "TARGETLASTOBSERVEDTIMESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.DiscoveryInfo.Level != DiscoveryLevels.Owned && targetVessel.DiscoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.Presence))
                //        {
                //            return Math.Max(Planetarium.GetUniversalTime() - targetVessel.DiscoveryInfo.lastObservedTime, 0.0);
                //        }
                //        else
                //        {
                //            return -1.0;
                //        }
                //    };

                //case "TARGETSIZECLASS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.DiscoveryInfo.Level != DiscoveryLevels.Owned && targetVessel.DiscoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.Presence))
                //        {
                //            return targetVessel.DiscoveryInfo.objectSize;
                //        }
                //        else
                //        {
                //            return "";
                //        }
                //    };

                //case "TARGETDISTANCEX":    //distance to target along the yaw axis (j and l rcs keys)
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3d.Dot(targetSeparation, vessel.GetTransform().right); };
                //case "TARGETDISTANCEY":   //distance to target along the pitch axis (i and k rcs keys)
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Vector3d.Dot(targetSeparation, vessel.GetTransform().forward); };
                //case "TARGETDISTANCEZ":  //closure distance from target - (h and n rcs keys)
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return -Vector3d.Dot(targetSeparation, vessel.GetTransform().up); };

                //case "TARGETDISTANCESCALEDX":    //scaled and clamped version of TARGETDISTANCEX.  Returns a number between 100 and -100, with precision increasing as distance decreases.
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        double scaledX = Vector3d.Dot(targetSeparation, vessel.GetTransform().right);
                //        double zdist = -Vector3d.Dot(targetSeparation, vessel.GetTransform().up);
                //        if (zdist < .1)
                //            scaledX = scaledX / (0.1 * Math.Sign(zdist));
                //        else
                //            scaledX = ((scaledX + zdist) / (zdist + zdist)) * (100) - 50;
                //        if (scaledX > 100) scaledX = 100;
                //        if (scaledX < -100) scaledX = -100;
                //        return scaledX;
                //    };


                //case "TARGETDISTANCESCALEDY":  //scaled and clamped version of TARGETDISTANCEY.  These two numbers will control the position needles on a docking port alignment gauge.
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        double scaledY = Vector3d.Dot(targetSeparation, vessel.GetTransform().forward);
                //        double zdist2 = -Vector3d.Dot(targetSeparation, vessel.GetTransform().up);
                //        if (zdist2 < .1)
                //            scaledY = scaledY / (0.1 * Math.Sign(zdist2));
                //        else
                //            scaledY = ((scaledY + zdist2) / (zdist2 + zdist2)) * (100) - 50;
                //        if (scaledY > 100) scaledY = 100;
                //        if (scaledY < -100) scaledY = -100;
                //        return scaledY;
                //    };

                //// TODO: I probably should return something else for vessels. But not sure what exactly right now.
                //case "TARGETANGLEX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            if (targetDockingNode != null)
                //                return JUtil.NormalAngle(-targetDockingNode.GetTransform().forward, FlightGlobals.ActiveVessel.ReferenceTransform.up, FlightGlobals.ActiveVessel.ReferenceTransform.forward);
                //            if (target is Vessel)
                //                return JUtil.NormalAngle(-target.GetFwdVector(), forward, up);
                //            return 0d;
                //        }
                //        return 0d;
                //    };
                //case "TARGETANGLEY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            if (targetDockingNode != null)
                //                return JUtil.NormalAngle(-targetDockingNode.GetTransform().forward, FlightGlobals.ActiveVessel.ReferenceTransform.up, -FlightGlobals.ActiveVessel.ReferenceTransform.right);
                //            if (target is Vessel)
                //            {
                //                JUtil.NormalAngle(-target.GetFwdVector(), forward, -right);
                //            }
                //            return 0d;
                //        }
                //        return 0d;
                //    };
                //case "TARGETANGLEZ":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            if (targetDockingNode != null)
                //                return (360 - (JUtil.NormalAngle(-targetDockingNode.GetTransform().up, FlightGlobals.ActiveVessel.ReferenceTransform.forward, FlightGlobals.ActiveVessel.ReferenceTransform.up))) % 360;
                //            if (target is Vessel)
                //            {
                //                return JUtil.NormalAngle(target.GetTransform().up, up, -forward);
                //            }
                //            return 0d;
                //        }
                //        return 0d;
                //    };
                //case "TARGETANGLEDEV":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null)
                //        {
                //            return Vector3d.Angle(vessel.ReferenceTransform.up, FlightGlobals.fetch.vesselTargetDirection);
                //        }
                //        return 180d;
                //    };

                //case "TARGETAPOAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.ApA;
                //        return double.NaN;
                //    };
                //case "TARGETPERIAPSIS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.PeA;
                //        return double.NaN;
                //    };
                //case "TARGETINCLINATION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.inclination;
                //        return double.NaN;
                //    };
                //case "TARGETECCENTRICITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.eccentricity;
                //        return double.NaN;
                //    };
                //case "TARGETORBITALVEL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.orbitalSpeed;
                //        return double.NaN;
                //    };
                //case "TARGETTIMETOAPSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.timeToAp;
                //        return double.NaN;
                //    };
                //case "TARGETORBPERIODSECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbit != null && targetOrbitSensibility)
                //            return targetOrbit.period;
                //        return double.NaN;
                //    };
                //case "TARGETTIMETOPESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (target != null && targetOrbitSensibility)
                //            return targetOrbit.eccentricity < 1 ?
                //                targetOrbit.timeToPe :
                //                -targetOrbit.meanAnomaly / (2 * Math.PI / targetOrbit.period);
                //        return double.NaN;
                //    };
                //case "TARGETLAUNCHTIMESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.mainBody == vessel.mainBody && (vessel.situation == Vessel.Situations.LANDED || vessel.situation == Vessel.Situations.PRELAUNCH || vessel.situation == Vessel.Situations.SPLASHED))
                //        {
                //            // MOARdV TODO: Make phase angle a variable?
                //            return TimeToPhaseAngle(12.7, vessel.mainBody, vessel.longitude, target.GetOrbit());
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };
                //case "TARGETPLANELAUNCHTIMESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetVessel != null && targetVessel.mainBody == vessel.mainBody && (vessel.situation == Vessel.Situations.LANDED || vessel.situation == Vessel.Situations.PRELAUNCH || vessel.situation == Vessel.Situations.SPLASHED))
                //        {
                //            return TimeToPlane(vessel.mainBody, vessel.latitude, vessel.longitude, target.GetOrbit());
                //        }
                //        else
                //        {
                //            return 0.0;
                //        }
                //    };

                //// Protractor-type values (phase angle, ejection angle)
                //case "TARGETBODYPHASEANGLE":
                //    // targetOrbit is always null if targetOrbitSensibility is false,
                //    // so no need to test if the orbit makes sense.
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.PhaseAngle;
                //    };
                //case "TARGETBODYPHASEANGLESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.TimeToPhaseAngle;
                //    };
                //case "TARGETBODYEJECTIONANGLE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.EjectionAngle;
                //    };
                //case "TARGETBODYEJECTIONANGLESECS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.TimeToEjectionAngle;
                //    };
                //case "TARGETBODYCLOSESTAPPROACH":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (orbitSensibility == true)
                //        {
                //            double approachTime;
                //            return JUtil.GetClosestApproach(vessel.GetOrbit(), target, out approachTime);
                //        }
                //        else
                //        {
                //            return -1.0;
                //        }
                //    };
                //case "TARGETBODYMOONEJECTIONANGLE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.MoonEjectionAngle;
                //    };
                //case "TARGETBODYEJECTIONALTITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.EjectionAltitude;
                //    };
                //case "TARGETBODYDELTAV":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        Protractor.Update(vessel, altitudeASL, targetOrbit);
                //        return Protractor.TargetBodyDeltaV;
                //    };

                //case "PREDICTEDLANDINGALTITUDE":
                //    return LandingAltitude();
                //case "PREDICTEDLANDINGLATITUDE":
                //    return LandingLatitude();
                //case "PREDICTEDLANDINGLONGITUDE":
                //    return LandingLongitude();
                //case "PREDICTEDLANDINGERROR":
                //    return LandingError();

                //// FLight control status
                //case "THROTTLE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.mainThrottle; };
                //case "STICKPITCH":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.pitch; };
                //case "STICKROLL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.roll; };
                //case "STICKYAW":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.yaw; };
                //case "STICKPITCHTRIM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.pitchTrim; };
                //case "STICKROLLTRIM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.rollTrim; };
                //case "STICKYAWTRIM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.yawTrim; };
                //case "STICKRCSX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.X; };
                //case "STICKRCSY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.Y; };
                //case "STICKRCSZ":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ctrlState.Z; };
                //case "PRECISIONCONTROL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (FlightInputHandler.fetch.precisionMode).GetHashCode(); };

                //// Staging and other stuff
                //case "STAGE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return StageManager.CurrentStage; };
                //case "STAGEREADY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (StageManager.CanSeparate && InputLockManager.IsUnlocked(ControlTypes.STAGING)).GetHashCode(); };
                //case "SITUATION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return SituationString(vessel.situation); };
                //case "RANDOM":
                //    cacheable = false;
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return UnityEngine.Random.value; };
                //case "RANDOMNORMAL":
                //    cacheable = false;
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        // Box-Muller method tweaked to prevent a 0 in u.
                //        float u = UnityEngine.Random.Range(0.0009765625f, 1.0f);
                //        float v = UnityEngine.Random.Range(0.0f, 2.0f * Mathf.PI);
                //        float x = Mathf.Sqrt(-2.0f * Mathf.Log(u)) * Mathf.Cos(v);
                //        // TODO: verify the stddev - I believe it is 1; mean is 0.
                //        return x;
                //    };
                //case "PODTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.temperature + KelvinToCelsius) : 0.0; };
                //case "PODTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.temperature) : 0.0; };
                //case "PODSKINTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.skinTemperature + KelvinToCelsius) : 0.0; };
                //case "PODSKINTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.skinTemperature) : 0.0; };
                //case "PODMAXSKINTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.skinMaxTemp + KelvinToCelsius) : 0.0; };
                //case "PODMAXSKINTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.skinMaxTemp) : 0.0; };
                //case "PODMAXTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.maxTemp + KelvinToCelsius) : 0.0; };
                //case "PODMAXTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.maxTemp) : 0.0; };
                //case "PODNETFLUX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null) ? (rpmComp.part.thermalConductionFlux + rpmComp.part.thermalConvectionFlux + rpmComp.part.thermalInternalFlux + rpmComp.part.thermalRadiationFlux) : 0.0; };
                //case "EXTERNALTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.externalTemperature + KelvinToCelsius; };
                //case "EXTERNALTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.externalTemperature; };
                //case "AMBIENTTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.atmosphericTemperature + KelvinToCelsius; };
                //case "AMBIENTTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.atmosphericTemperature; };
                //case "HEATSHIELDTEMPERATURE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (double)heatShieldTemperature + KelvinToCelsius; };
                //case "HEATSHIELDTEMPERATUREKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return heatShieldTemperature; };
                //case "HEATSHIELDTEMPERATUREFLUX":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return heatShieldFlux; };
                //case "HOTTESTPARTTEMP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return hottestPartTemperature; };
                //case "HOTTESTPARTMAXTEMP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return hottestPartMaxTemperature; };
                //case "HOTTESTPARTTEMPRATIO":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (hottestPartMaxTemperature > 0.0f) ? (hottestPartTemperature / hottestPartMaxTemperature) : 0.0f; };
                //case "HOTTESTPARTNAME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return hottestPartName; };
                //case "HOTTESTENGINETEMP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return hottestEngineTemperature; };
                //case "HOTTESTENGINEMAXTEMP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return hottestEngineMaxTemperature; };
                //case "HOTTESTENGINETEMPRATIO":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (hottestEngineMaxTemperature > 0.0f) ? (hottestEngineTemperature / hottestEngineMaxTemperature) : 0.0f; };
                //case "SLOPEANGLE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return slopeAngle; };
                //case "SPEEDDISPLAYMODE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        switch (FlightGlobals.speedDisplayMode)
                //        {
                //            case FlightGlobals.SpeedDisplayModes.Orbit:
                //                return 1d;
                //            case FlightGlobals.SpeedDisplayModes.Surface:
                //                return 0d;
                //            case FlightGlobals.SpeedDisplayModes.Target:
                //                return -1d;
                //        }
                //        return double.NaN;
                //    };
                //case "ISONKERBINTIME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return GameSettings.KERBIN_TIME.GetHashCode(); };
                //case "ISDOCKINGPORTREFERENCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        ModuleDockingNode thatPort = null;
                //        Part referencePart = rpmComp.vessel.GetReferenceTransformPart();
                //        if (referencePart != null)
                //        {
                //            foreach (PartModule thatModule in referencePart.Modules)
                //            {
                //                thatPort = thatModule as ModuleDockingNode;
                //                if (thatPort != null)
                //                    break;
                //            }
                //        }
                //        if (thatPort != null)
                //            return 1d;
                //        return 0d;
                //    };
                //case "ISCLAWREFERENCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        ModuleGrappleNode thatClaw = null;
                //        Part referencePart = rpmComp.vessel.GetReferenceTransformPart();
                //        if (referencePart != null)
                //        {
                //            foreach (PartModule thatModule in referencePart.Modules)
                //            {
                //                thatClaw = thatModule as ModuleGrappleNode;
                //                if (thatClaw != null)
                //                    break;
                //            }
                //        }
                //        if (thatClaw != null)
                //            return 1d;
                //        return 0d;
                //    };
                //case "ISREMOTEREFERENCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        ModuleCommand thatPod = null;
                //        Part referencePart = rpmComp.vessel.GetReferenceTransformPart();
                //        if (referencePart != null)
                //        {
                //            foreach (PartModule thatModule in referencePart.Modules)
                //            {
                //                thatPod = thatModule as ModuleCommand;
                //                if (thatPod != null)
                //                    break;
                //            }
                //        }
                //        if (thatPod == null)
                //            return 1d;
                //        return 0d;
                //    };
                //case "FLIGHTUIMODE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        switch (FlightUIModeController.Instance.Mode)
                //        {
                //            case FlightUIMode.DOCKING:
                //                return 1d;
                //            case FlightUIMode.STAGING:
                //                return -1d;
                //            case FlightUIMode.ORBITAL:
                //                return 0d;
                //        }
                //        return double.NaN;
                //    };

                //// Meta.
                //case "RPMVERSION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion; };
                // That would return only the "AssemblyVersion" version which in our case does not change anymore.
                // We use "AsssemblyFileVersion" for actual version numbers now to facilitate hardlinking.
                // return Assembly.GetExecutingAssembly().GetName().Version.ToString();

                //case "MECHJEBAVAILABLE":
                //    return MechJebAvailable();

                //case "TIMEWARPPHYSICS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (TimeWarp.CurrentRate > 1.0f && TimeWarp.WarpMode == TimeWarp.Modes.LOW).GetHashCode(); };
                //case "TIMEWARPNONPHYSICS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (TimeWarp.CurrentRate > 1.0f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH).GetHashCode(); };
                //case "TIMEWARPACTIVE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (TimeWarp.CurrentRate > 1.0f).GetHashCode(); };
                //case "TIMEWARPCURRENT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return TimeWarp.CurrentRate; };

                //// Compound variables which exist to stave off the need to parse logical and arithmetic expressions. :)
                //case "GEARALARM":
                //    // Returns 1 if vertical speed is negative, gear is not extended, and radar altitude is less than 50m.
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (speedVerticalRounded < 0 && !vessel.ActionGroups.groups[RPMVesselComputer.gearGroupNumber] && altitudeBottom < 100).GetHashCode(); };
                //case "GROUNDPROXIMITYALARM":
                //    // Returns 1 if, at maximum acceleration, in the time remaining until ground impact, it is impossible to get a vertical speed higher than -10m/s.
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (SpeedAtImpact(totalLimitedMaximumThrust) < -10d).GetHashCode(); };
                //case "TUMBLEALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (speedVerticalRounded < 0 && altitudeBottom < 100 && speedHorizontal > 5).GetHashCode(); };
                //case "SLOPEALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (speedVerticalRounded < 0.0 && altitudeBottom < 100.0 && slopeAngle > 15.0f).GetHashCode(); };
                //case "DOCKINGANGLEALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return (targetDockingNode != null && targetDistance < 10 && approachSpeed > 0.0f &&
                //            (Math.Abs(JUtil.NormalAngle(-targetDockingNode.GetFwdVector(), forward, up)) > 1.5 ||
                //            Math.Abs(JUtil.NormalAngle(-targetDockingNode.GetFwdVector(), forward, -right)) > 1.5)).GetHashCode();
                //    };
                //case "DOCKINGSPEEDALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (targetDockingNode != null && approachSpeed > 2.5f && targetDistance < 15).GetHashCode(); };
                //case "ALTITUDEALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (speedVerticalRounded < 0 && altitudeBottom < 150).GetHashCode(); };
                //case "PODTEMPERATUREALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (rpmComp != null && rpmComp.part != null)
                //        {
                //            double tempRatio = rpmComp.part.temperature / rpmComp.part.maxTemp;
                //            if (tempRatio > 0.85d)
                //            {
                //                return 1d;
                //            }
                //            else if (tempRatio > 0.75d)
                //            {
                //                return 0d;
                //            }
                //        }
                //        return -1d;
                //    };
                //// Well, it's not a compound but it's an alarm...
                //case "ENGINEOVERHEATALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return anyEnginesOverheating.GetHashCode(); };
                //case "ENGINEFLAMEOUTALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return anyEnginesFlameout.GetHashCode(); };
                //case "IMPACTALARM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (rpmComp != null && rpmComp.part != null && vessel.srfSpeed > rpmComp.part.crashTolerance).GetHashCode(); };

                //// SCIENCE!!
                //case "SCIENCEDATA":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalDataAmount; };
                //case "SCIENCECOUNT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return totalExperimentCount; };
                //case "BIOMENAME":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.CurrentBiome(); };
                //case "BIOMEID":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return ScienceUtil.GetExperimentBiome(vessel.mainBody, vessel.latitude, vessel.longitude); };

                //// Some of the new goodies in 0.24.
                //case "REPUTATION":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Reputation.Instance != null ? Reputation.CurrentRep : 0.0f; };
                //case "FUNDS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Funding.Instance != null ? Funding.Instance.Funds : 0.0; };


                //// Action group flags. To properly format those, use this format:
                //// {0:on;0;OFF}
                //case "GEAR":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ActionGroups.groups[RPMVesselComputer.gearGroupNumber].GetHashCode(); };
                //case "BRAKES":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ActionGroups.groups[RPMVesselComputer.brakeGroupNumber].GetHashCode(); };
                //case "SAS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ActionGroups.groups[RPMVesselComputer.sasGroupNumber].GetHashCode(); };
                //case "LIGHTS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ActionGroups.groups[RPMVesselComputer.lightGroupNumber].GetHashCode(); };
                //case "RCS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.ActionGroups.groups[RPMVesselComputer.rcsGroupNumber].GetHashCode(); };
                //// 0.90 SAS mode fields:
                //case "SASMODESTABILITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.StabilityAssist) ? 1.0 : 0.0; };
                //case "SASMODEPROGRADE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.Prograde) ? 1.0 :
                //            (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.Retrograde) ? -1.0 : 0.0;
                //    };
                //case "SASMODENORMAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.Normal) ? 1.0 :
                //            (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.Antinormal) ? -1.0 : 0.0;
                //    };
                //case "SASMODERADIAL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.RadialOut) ? 1.0 :
                //            (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.RadialIn) ? -1.0 : 0.0;
                //    };
                //case "SASMODETARGET":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.Target) ? 1.0 :
                //            (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.AntiTarget) ? -1.0 : 0.0;
                //    };
                //case "SASMODEMANEUVER":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return (vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.Maneuver) ? 1.0 : 0.0; };

                // Database information about planetary bodies.
                //case "ORBITBODYATMOSPHERE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.atmosphere ? 1d : -1d; };
                //case "TARGETBODYATMOSPHERE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.atmosphere ? 1d : -1d;
                //        return 0d;
                //    };
                //case "ORBITBODYOXYGEN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.atmosphereContainsOxygen ? 1d : -1d; };
                //case "TARGETBODYOXYGEN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.atmosphereContainsOxygen ? 1d : -1d;
                //        return -1d;
                //    };
                //case "ORBITBODYSCALEHEIGHT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.atmosphereDepth; };
                //case "TARGETBODYSCALEHEIGHT":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.atmosphereDepth;
                //        return -1d;
                //    };
                //case "ORBITBODYRADIUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.Radius; };
                //case "TARGETBODYRADIUS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.Radius;
                //        return -1d;
                //    };
                //case "ORBITBODYMASS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.Mass; };
                //case "TARGETBODYMASS":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.Mass;
                //        return -1d;
                //    };
                //case "ORBITBODYROTATIONPERIOD":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.rotationPeriod; };
                //case "TARGETBODYROTATIONPERIOD":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.rotationPeriod;
                //        return -1d;
                //    };
                //case "ORBITBODYSOI":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.sphereOfInfluence; };
                //case "TARGETBODYSOI":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.sphereOfInfluence;
                //        return -1d;
                //    };
                //case "ORBITBODYGEEASL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.GeeASL; };
                //case "TARGETBODYGEEASL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.GeeASL;
                //        return -1d;
                //    };
                //case "ORBITBODYGM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.gravParameter; };
                //case "TARGETBODYGM":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.gravParameter;
                //        return -1d;
                //    };
                //case "ORBITBODYATMOSPHERETOP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return vessel.orbit.referenceBody.atmosphereDepth; };
                //case "TARGETBODYATMOSPHERETOP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return targetBody.atmosphereDepth;
                //        return -1d;
                //    };
                //case "ORBITBODYESCAPEVEL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return Math.Sqrt(2 * vessel.orbit.referenceBody.gravParameter / vessel.orbit.referenceBody.Radius); };
                //case "TARGETBODYESCAPEVEL":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return Math.Sqrt(2 * targetBody.gravParameter / targetBody.Radius);
                //        return -1d;
                //    };
                //case "ORBITBODYAREA":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return 4 * Math.PI * vessel.orbit.referenceBody.Radius * vessel.orbit.referenceBody.Radius; };
                //case "TARGETBODYAREA":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //            return 4 * Math.PI * targetBody.Radius * targetBody.Radius;
                //        return -1d;
                //    };
                //case "ORBITBODYSYNCORBITALTITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        double syncRadius = Math.Pow(vessel.orbit.referenceBody.gravParameter / Math.Pow(2 * Math.PI / vessel.orbit.referenceBody.rotationPeriod, 2), 1 / 3d);
                //        return syncRadius > vessel.orbit.referenceBody.sphereOfInfluence ? double.NaN : syncRadius - vessel.orbit.referenceBody.Radius;
                //    };
                //case "TARGETBODYSYNCORBITALTITUDE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //        {
                //            double syncRadiusT = Math.Pow(targetBody.gravParameter / Math.Pow(2 * Math.PI / targetBody.rotationPeriod, 2), 1 / 3d);
                //            return syncRadiusT > targetBody.sphereOfInfluence ? double.NaN : syncRadiusT - targetBody.Radius;
                //        }
                //        return -1d;
                //    };
                //case "ORBITBODYSYNCORBITVELOCITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        return (2 * Math.PI / vessel.orbit.referenceBody.rotationPeriod) *
                //            Math.Pow(vessel.orbit.referenceBody.gravParameter / Math.Pow(2 * Math.PI / vessel.orbit.referenceBody.rotationPeriod, 2), 1 / 3d);
                //    };
                //case "TARGETBODYSYNCORBITVELOCITY":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //        {
                //            return (2 * Math.PI / targetBody.rotationPeriod) *
                //            Math.Pow(targetBody.gravParameter / Math.Pow(2 * Math.PI / targetBody.rotationPeriod, 2), 1 / 3d);
                //        }
                //        return -1d;
                //    };
                //case "ORBITBODYSYNCORBITCIRCUMFERENCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return 2 * Math.PI * Math.Pow(vessel.orbit.referenceBody.gravParameter / Math.Pow(2 * Math.PI / vessel.orbit.referenceBody.rotationPeriod, 2), 1 / 3d); };
                //case "TARGETBODYSYNCORBICIRCUMFERENCE":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //        {
                //            return 2 * Math.PI * Math.Pow(targetBody.gravParameter / Math.Pow(2 * Math.PI / targetBody.rotationPeriod, 2), 1 / 3d);
                //        }
                //        return -1d;
                //    };
                //case "ORBITBODYSURFACETEMP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return FlightGlobals.currentMainBody.atmosphereTemperatureSeaLevel + KelvinToCelsius; };
                //case "TARGETBODYSURFACETEMP":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //        {
                //            return targetBody.atmosphereTemperatureSeaLevel + KelvinToCelsius;
                //        }
                //        return -1d;
                //    };
                //case "ORBITBODYSURFACETEMPKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) => { return FlightGlobals.currentMainBody.atmosphereTemperatureSeaLevel; };
                //case "TARGETBODYSURFACETEMPKELVIN":
                //    return (string variable, RasterPropMonitorComputer rpmComp) =>
                //    {
                //        if (targetBody != null)
                //        {
                //            return targetBody.atmosphereTemperatureSeaLevel;
                //        }
                //        return -1d;
                //    };
            //}

            return null;
        }
        #endregion
    }
}
