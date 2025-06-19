using log4net;
using OSIsoft.AF;
using OSIsoft.AF.Analysis;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Search;
using OSIsoft.AF.Time;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

/*
 *  Copyright (C) 2017  Keith Fong

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

namespace AnalysisBackfill
{
    class AnalysisBackfill
    {

        public static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        static void Main(string[] args)
        {
            //define variables
            string user_path = null;
            string user_serv = null;
            string user_db = null;
            string user_analysisfilter = null;
            string user_mode = null;

            log4net.Config.XmlConfigurator.Configure();

            PISystems aSystems = new PISystems();
            PISystem aSystem = null;
            AFAnalysisService aAnalysisService = null;
            AFDatabase aDatabase = null;
            List<AFElement> foundElements = new List<AFElement>();
            List<AFAnalysis> foundAnalyses = new List<AFAnalysis>();

            AFTimeRange backfillPeriod = new AFTimeRange();

            AFAnalysisService.CalculationMode mode = AFAnalysisService.CalculationMode.FillDataGaps;
            Object response = null;

            String help_message = "This utility backfills/recalculates analyses.  Generic syntax: "
                            + "\n\tAnalysisBackfill.exe \\\\AFServer\\AFDatabase\\pathToElement\\AFElement AnalysisNameFilter StartTime EndTime Mode"
                            + "\n This utility supports two modes: backfill and recalc.  Backfill will fill in data gaps only.  Recalc will replace all values.  Examples:"
                            + "\n\tAnalysisBackfill.exe \\\\AF1\\TestDB\\Plant1\\Pump1 FlowRate_*Avg '*-10d' '*' recalc"
                            + "\n\tAnalysisBackfill.exe \\\\AF1\\TestDB\\Plant1\\Pump1 *Rollup '*-10d' '*' backfill";

            //bad input handling & help
            if (args.Length < 5 || args.Contains("?"))
            {
                Logger.Info(help_message);
                Environment.Exit(0);
            }

            try
            {
                //parse inputs and connect
                user_path = args[0];
                var inputs = user_path.Split('\\');
                user_serv = inputs[2];
                user_db = inputs[3];

                //connect
                AFSystemHelper.Connect(user_serv, user_db);
                aSystem = aSystems[user_serv];
                aDatabase = aSystem.Databases[user_db];
                aAnalysisService = aSystem.AnalysisService;

                //other inputs
                user_analysisfilter = args[1];
                AFTime backfillStartTime = new AFTime(args[2].Trim('\''));
                AFTime backfillEndTime = new AFTime(args[3].Trim('\''));
                backfillPeriod = new AFTimeRange(backfillStartTime, backfillEndTime);

                //user_mode
                user_mode = args[4];
                switch (user_mode.ToLower())
                {
                    case "recalc":
                        mode = AFAnalysisService.CalculationMode.DeleteExistingData;
                        break;
                    case "backfill":
                        mode = AFAnalysisService.CalculationMode.FillDataGaps;
                        break;
                    default:
                        Logger.Warn("Invalid mode specified.  Supported modes: backfill, recalc");
                        Environment.Exit(0);
                        break;
                }

                Logger.Info("Requested backfills/recalculations:");

                String analysisfilter = "Category:'" + user_analysisfilter + "'";
                AFAnalysisSearch analysisSearch = new AFAnalysisSearch(aDatabase, "AnalysesByCategorySearch", AFAnalysisSearch.ParseQuery(analysisfilter));
                foundAnalyses.AddRange(analysisSearch.FindAnalyses(0, true).ToList());

                Logger.Info($"\nTime range: {backfillPeriod.ToString()}, {backfillPeriod.Span.Days}d {backfillPeriod.Span.Hours}h {backfillPeriod.Span.Minutes}m {backfillPeriod.Span.Seconds}s.");
                Logger.Info("Mode: " + user_mode + "=" + mode.ToString());
                //implement wait time
                Logger.Info($"\nA total of {foundAnalyses.Count} analyses will be queued for processing in 5 seconds.  Press Ctrl+C to cancel.");
                DateTime beginWait = DateTime.Now;
                while (!Console.KeyAvailable && DateTime.Now.Subtract(beginWait).TotalSeconds < 5)
                {
                    Console.Write(".");
                    Thread.Sleep(250);
                }
                //no status check
                Logger.Info($"\n\nAll analyses have been queued.\nThere is no status check after the backfill/recalculate is queued (until AF 2.9.0). Please verify by using other means. {foundAnalyses.Count}");

                //aAnalysisService.QueueCalculation(foundAnalyses, backfillPeriod, mode);

                //queue analyses for backfill/recalc
                // below queues them all one at a time , which is not efficient, but it is the only way to check status in AF 2.8.5
                foreach (var analysis_n in foundAnalyses)
                {
                    response = aAnalysisService.QueueCalculation(new List<AFAnalysis> { analysis_n }, backfillPeriod, mode);

                    /* no status check info
                        * in AF 2.9, QueueCalculation will allow for true status checking. In AF 2.8.5, it is not possible to check.  
                        * Documentation (https://techsupport.osisoft.com/Documentation/PI-AF-SDK/html/M_OSIsoft_AF_Analysis_AFAnalysisService_ToString.htm) states:
                        *This method queues the list of analyses on the analysis service to be calculated. 
                        * The operation is asynchronous and returning of the method does not indicate that queued analyses were calculated. 
                        * The status can be queried in the upcoming releases using the returned handle.
                    */

                    //Might be able to add a few check mechanisms using AFAnalysis.GetResolvedOutputs and the number of values in AFTimeRange
                }

            }
            catch (Exception ex)
            {
                Logger.Warn("Error returned: " + ex.Message);
                Environment.Exit(0);
            }
        }
    }
}
