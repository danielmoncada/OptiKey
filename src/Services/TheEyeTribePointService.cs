﻿using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using JuliusSweetland.OptiKey.Properties;
using log4net;
using TETCSharpClient;
using TETCSharpClient.Data;

namespace JuliusSweetland.OptiKey.Services
{
    public class TheEyeTribePointService : ITheEyeTribePointService
    {
        #region Fields

        private readonly static ILog Log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private event EventHandler<Timestamped<Point>> pointEvent;

        #endregion

        #region Ctor

        public TheEyeTribePointService()
        {
            //Disconnect (deactivate) from the TET server on shutdown - otherwise the process can hang
            Application.Current.Exit += (sender, args) =>
            {
                if (GazeManager.Instance.IsActivated)
                {
                    Log.Debug("Deactivating TheEyeTribe's GazeManager.");
                    GazeManager.Instance.Deactivate();
                }
            };
        }

        #endregion

        #region Events

        public event EventHandler<Exception> Error;

        public event EventHandler<Timestamped<Point>> Point
        {
            add
            {
                if (pointEvent == null)
                {
                    Log.Debug("Point event has first subscriber.");

                    //Activate TET if required
                    if (!GazeManager.Instance.IsActivated)
                    {
                        Log.Debug("Attempting to activate TheEyeTribe's GazeManager...");

                        var success = GazeManager.Instance.Activate(GazeManager.ApiVersion.VERSION_1_0, GazeManager.ClientMode.Push); //Connect client
                        if (success)
                        {
                            Log.InfoFormat("...activation {0}", success ? "was successful." : "failed!");
                        }
                        else
                        {
                            PublishError(this, new ApplicationException("TheEyeTribe cannot be reached! Please check that the server is running."));
                        }
                    }

                    //Add this class as a gaze listener for TET updates
                    if (GazeManager.Instance.IsActivated
                        && !GazeManager.Instance.HasGazeListener(this))
                    {
                        Log.Info("Attempting to add myself as a gaze listener to TheEyeTribe server.");
                        GazeManager.Instance.AddGazeListener(this); //Register this class for updates
                    }

                    //Publish error if TET not calibrated
                    if (GazeManager.Instance.IsActivated
                        && !GazeManager.Instance.IsCalibrated)
                    {
                        Log.DebugFormat("TheEyeTribe server is reporting that it is not calibrated - retrying for up to {0}ms", 
                            Settings.Default.TETCalibrationCheckTimeSpan.TotalMilliseconds);

                        var calibrated = false;
                        var retryStart = DateTimeOffset.Now.ToUniversalTime();
                        while (DateTimeOffset.Now.ToUniversalTime().Subtract(retryStart.ToUniversalTime()) 
                            < Settings.Default.TETCalibrationCheckTimeSpan)
                        {
                            if (GazeManager.Instance.IsCalibrated)
                            {
                                Log.Debug("TheEyeTribe server is now reporting that it is calibrated - moving on");
                                calibrated = true;
                                break;
                            }
                        }

                        if (!calibrated)
                        {
                            PublishError(this, new ApplicationException("TheEyeTribe has not been calibrated. No data will be received until calibration is completed."));
                        }
                    }
                }

                pointEvent += value;
            }
            remove
            {
                pointEvent -= value;

                if (pointEvent == null)
                {
                    Log.Info("Last listener of Point event has unsubscribed. Disconnecting from server...");
                    var success = GazeManager.Instance.RemoveGazeListener(this); //Unregister this class for updates
                    GazeManager.Instance.Deactivate(); //Disconnect client
                    Log.InfoFormat("...disconnect {0}", success ? "was successful." : "failed!");
                }
            }
        }

        #endregion

        #region On Gaze Update Event Handler

        public void OnGazeUpdate(GazeData data)
        {
            if (GazeManager.Instance.IsCalibrated
                && pointEvent != null)
            {
                pointEvent(this, new Timestamped<Point>(
                    new Point(data.SmoothedCoordinates.X, data.SmoothedCoordinates.Y),
                    new DateTimeOffset(DateTime.Parse(data.TimeStampString)).ToUniversalTime()));
            }
        }

        #endregion

        #region Publish Error

        private void PublishError(object sender, Exception ex)
        {
            Log.Error("Publishing Error event (if there are any listeners)", ex);
            if (Error != null)
            {
                Error(sender, ex);
            }
        }

        #endregion
    }
}