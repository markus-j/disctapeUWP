using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using disctape.Common;
using Windows.Devices.Geolocation;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.Phone.UI.Input;
using disctape;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace disctapeWP10
{
    public enum DistanceType { MILES, KILOMETERS, METERS, FEET, METERES_AND_FEET };
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();
        private Geolocator geolocator = new Geolocator();
        //private DispatcherTimer timer = new DispatcherTimer();


        private Geoposition start_position1;
        private Geoposition start_position2;
        private Geoposition start_position3;
        private BasicGeoposition avg_start_position;
        private DateTime start_position_timestamp;

        private bool isGeolocatorInitialized = false;
        private bool clearStatusMsg = false;
        private bool start_position_set = false;
        private int start_position_count = 0;
        private int accuracy_update_counter = 0;
        private double avg_start_point_accuracy = 0;
        private double last_failed_position_accuracy = 0;
        private double result_m = 0;
        private double result_ft = 0;

        private TimeSpan MAX_CACHE_AGE_TIMESPAN = new TimeSpan(1);
        private TimeSpan MAX_WAIT_TIMESPAN = new TimeSpan(0, 0, 5);
        private const int GETLOCATIONS_SLEEP_TIME_MS = 600;
        private const int REPORT_INTERVAL_START = 500;
        private const int REPORT_INTERVAL_RUNTIME = 1000;
        private const int DESIRED_ACCURACY = 1;
        private const int GOOD_ACCURACY_IN_METERS = 4;
        private const int MEDIUM_ACCURACY_IN_METERS = 8;
        private const int POOR_ACCURACY_IN_METERS = 12;
        private const int MAX_ACCURACY = 15;
        private const int MAX_GETLOCATION_RETRY_COUNT = 6;
        private const int MOVEMENT_TRESHOLD = 1;
        private const int ACCURACY_UPDATE_FREQUENCY = 4;
        private const double MAX_START_POSITION_AGE_IN_HOURS = 6;
        private const double FONT_SIZE_INCREMENT = 10;
        private const double FONT_SIZE_INCREMENT_TRESHOLD = 40;
        private const double MAX_RESULT_LABEL_FONT_SIZE = 230;
        private const double METER_IN_FEET = 3.2808399;
        private const string LOCATION_OPTED_OUT = "Location disabled.";
        private const string START_POINT_ACCURACY_TXT = "Start point accuracy:";
        private const string ACCURACY_TXT = "Result accuracy:";
        private const string GPS_ACCURACY_TXT = "GPS accuracy:";
        private const string GOOD_ACCURACY = "good";
        private const string MEDIUM_ACCURACY = "okay";
        private const string POOR_ACCURACY = "poor";
        private const int MIN_DISTANCE_M = 10;
        private const int MIN_DISTANCE_FT = 33;
        private const string SYSTEM_EXCEPTION = "Could not get accurate location.\nMake sure you are outside\nwith good visibility to the sky.";
        private const string TIMEOUT_EXCEPTION = "Could not get accurate location.\nMake sure you are outside\nwith good visibility to the sky.";
        private const string STARTING_GPS = "Starting GPS...";
        //private const string LOCATION_CONSENT_QUESTION = "Allow this app to access to your device's location?";
        private const string WAIT_ONE = "Getting your location...";
        private const string WAIT_TWO = "Getting your location...";
        private const string WAIT_THREE = "Getting your location...";
        private const string RESUMING = "Resuming...";
        private const string UNDER_MINIMUM_DISTANCE_TXT = "Ready, move forward";
        public MainPage()
        {
            this.InitializeComponent();

            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += this.NavigationHelper_LoadState;
            this.navigationHelper.SaveState += this.NavigationHelper_SaveState;

            //check display unit from localSettings, if not set, set it to meters
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            if (!localSettings.Values.ContainsKey("DisplayUnit"))
            {
                localSettings.Values["DisplayUnit"] = "METERS";
            }

            //connect label size change to its handler
            resultLbl_m.SizeChanged += resultLbl_SizeChanged;
            resultLbl_ft.SizeChanged += resultLbl_SizeChanged;

            
        }

        // when result label width changes, font size has to be adjusted to fit the screen
        void resultLbl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            TextBlock label = (TextBlock)sender;
            double newWidth = e.NewSize.Width;
            Debug.WriteLine("newWidth: " + newWidth);
            Debug.WriteLine("label.Parent as Grid).ActualWidth: " + (label.Parent as Grid).ActualWidth);

            if (newWidth > (label.Parent as Grid).ActualWidth)
            {
                if (label.Name == "resultLbl_m")
                {
                    resultLbl_m.FontSize -= FONT_SIZE_INCREMENT;
                }
                else if (label.Name == "resultLbl_ft")
                {
                    resultLbl_ft.FontSize -= FONT_SIZE_INCREMENT;
                }
            }
            else if (newWidth < (label.Parent as Grid).ActualWidth - FONT_SIZE_INCREMENT_TRESHOLD
                     && label.FontSize <= MAX_RESULT_LABEL_FONT_SIZE)
            {
                if (label.Name == "resultLbl_m")
                {
                    resultLbl_m.FontSize += FONT_SIZE_INCREMENT;
                }
                else if (label.Name == "resultLbl_ft")
                {
                    resultLbl_ft.FontSize += FONT_SIZE_INCREMENT;
                }
            }
        }

        private async void geolocator_PositionChanged(Geolocator sender, PositionChangedEventArgs args)
        {
            Debug.WriteLine("positionChanged, accuracy: " + args.Position.Coordinate.Accuracy);
            Debug.WriteLine("accuracy_update_counter: " + accuracy_update_counter);
            Debug.WriteLine("report interval: " + sender.ReportInterval);


            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                setStartBtn.IsEnabled = true;
            });

            //accuracy update first
            if (accuracy_update_counter == 0)
            {
                double total_avg_accuracy = 0.75 * avg_start_point_accuracy + 0.25 * args.Position.Coordinate.Accuracy;
                if (total_avg_accuracy <= GOOD_ACCURACY_IN_METERS)
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        accuracyLbl.Text = GOOD_ACCURACY;
                        accuracyLbl.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                        accuracyTxt.Text = ACCURACY_TXT;
                    });
                }
                else if (total_avg_accuracy <= MEDIUM_ACCURACY_IN_METERS)
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        accuracyLbl.Text = MEDIUM_ACCURACY;
                        accuracyLbl.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                        accuracyTxt.Text = ACCURACY_TXT;
                    });
                }
                else
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        accuracyLbl.Text = POOR_ACCURACY;
                        accuracyLbl.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
                        accuracyTxt.Text = ACCURACY_TXT;
                    });
                }
            }

            accuracy_update_counter++;
            if (accuracy_update_counter >= ACCURACY_UPDATE_FREQUENCY)
            {
                accuracy_update_counter = 0;
            }

            if (start_position_set)
            {
                double distance_m = 0;
                double distance_ft = 0;

                BasicGeoposition newPosition;
                newPosition.Latitude = args.Position.Coordinate.Point.Position.Latitude;
                newPosition.Longitude = args.Position.Coordinate.Point.Position.Longitude;
                newPosition.Altitude = args.Position.Coordinate.Point.Position.Altitude;

                distance_m = DistanceTo(avg_start_position, newPosition, DistanceType.KILOMETERS);
                distance_ft = distance_m * METER_IN_FEET;
                result_m = Math.Round(distance_m);
                result_ft = Math.Round(distance_ft);


                //var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

                Debug.WriteLine("positionChanged, distance_m: " + result_m + " distance_ft: " + result_ft);

                if (distance_m >= MIN_DISTANCE_M)
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        resultLbl_m.Text = result_m.ToString();
                        resultLbl_ft.Text = result_ft.ToString();
                        statusLbl.Text = "";
                        accuracyTxt.Text = ACCURACY_TXT;
                    });
                }
                else
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        resultLbl_m.Text = "0";
                        resultLbl_ft.Text = "0";
                        statusLbl.Text = UNDER_MINIMUM_DISTANCE_TXT;
                        accuracyTxt.Text = START_POINT_ACCURACY_TXT;
                    });
                    return;
                }
            }
        }

        /// <summary>  
        /// Returns the distance in   
        /// latitude / longitude points.  
        /// </summary>  
        private double DistanceTo(BasicGeoposition point1, BasicGeoposition point2, DistanceType type)
        {
            double R = (type == DistanceType.MILES) ? 3960 : 6371;
            double dLat = this.toRadian(point2.Latitude - point1.Latitude);
            double dLon = this.toRadian(point2.Longitude - point1.Longitude);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(this.toRadian(point1.Latitude)) * Math.Cos(this.toRadian(point2.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
            double d = R * c;

            double result_in_meters = d * 1000;
            return result_in_meters;
        }

        private double toRadian(double val)
        {
            return (Math.PI / 180) * val;
        }

        private async void setStartBtn_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("status1: " + geolocator.LocationStatus.ToString());
            // reset the counter to show the accuracy from the first positionChanged event
            accuracy_update_counter = 0;
            GeolocationAccessStatus status = await Geolocator.RequestAccessAsync();
            if (status == GeolocationAccessStatus.Denied)
            {
                setStartBtn.IsEnabled = false;
                statusLbl.Text = LOCATION_OPTED_OUT;
                settingsLink.Visibility = Visibility.Visible;
            }
            else
            {
                setGeolocatorToGetLocationsState();
                getLocations();
            }
        }


        private async void getLocations()
        {
            statusLbl.Text = WAIT_THREE;
            int i = 0;
            while (i < MAX_GETLOCATION_RETRY_COUNT)
            {
                // break early if not possible anymore to get location
                if (MAX_GETLOCATION_RETRY_COUNT - i < 3 && start_position_count == 0)
                {
                    Debug.WriteLine("Not possible anymore to get location");
                    //setGeolocatorToInitState(TIMEOUT_EXCEPTION);
                    break;
                }
                Geoposition position;
                try
                {
                    position = await geolocator.GetGeopositionAsync(MAX_CACHE_AGE_TIMESPAN, MAX_WAIT_TIMESPAN);
                }
                catch (System.UnauthorizedAccessException)
                {
                    statusLbl.Text = "No access to location data";
                    //detachGeolocator();
                    setStartBtn.IsEnabled = true;
                    progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    break;
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine("Task canceled exception");
                    statusLbl.Text = "Location not found";
                    //detachGeolocator();
                    i++;
                    continue;
                }
                catch (System.TimeoutException)
                {
                    Debug.WriteLine("Timeout Exception");
                    start_position_count = 0;
                    setStartBtn.IsEnabled = true;
                    progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

                    i++;
                    continue;
                    //setGeolocatorToInitState(TIMEOUT_EXCEPTION);
                }
                catch (System.Exception e)
                {
                    //statusLbl.Text = SYSTEM_EXCEPTION;
                    Debug.WriteLine("System Exception: " + e.Message);
                    start_position_count = 0;
                    i++;
                    continue;
                    //setGeolocatorToInitState(TIMEOUT_EXCEPTION);
                }
                if (!start_position_set)
                {
                    Debug.WriteLine("accuracy:" + position.Coordinate.Accuracy.ToString() + 
                        " position:" + position.Coordinate.Point.Position.Latitude.ToString() + "," + position.Coordinate.Point.Position.Longitude.ToString());
                    
                    // if the position is accurate enough, add it to the results
                    if (position.Coordinate.Accuracy <= MAX_ACCURACY)
                    {
                        if (start_position_count == 0)
                        {
                            start_position1 = position;
                            statusLbl.Text = WAIT_TWO;
                            last_failed_position_accuracy = 0;
                        }
                        else if (start_position_count == 1)
                        {
                            start_position2 = position;
                            statusLbl.Text = WAIT_ONE;
                            last_failed_position_accuracy = 0;
                        }
                        else
                        {
                            start_position3 = position;
                            start_position_count = 0;
                            calculateStartLocation();
                            setGeolocatorToReportState();
                            last_failed_position_accuracy = 0;
                            start_position_timestamp = DateTime.Now;
                            return;
                        }
                        start_position_count++;
                    }
                    else
                    {
                        if (last_failed_position_accuracy != 0 &&
                            position.Coordinate.Accuracy >= last_failed_position_accuracy)
                        {
                            Debug.WriteLine("current_accuracy: " + position.Coordinate.Accuracy.ToString() + " last_failed_acc: " + last_failed_position_accuracy.ToString());
                            last_failed_position_accuracy = 0;
                            break;
                        }
                        last_failed_position_accuracy = position.Coordinate.Accuracy;
                    }
                }
                i++;
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(GETLOCATIONS_SLEEP_TIME_MS));
            }
            // max number of tries and no start position
            if (!start_position_set)
            {
                Debug.WriteLine("max number of tries or break, no success");
                setGeolocatorToInitState(TIMEOUT_EXCEPTION);
            }
        }

        // calculate weighted average from the three points
        private void calculateStartLocation()
        {
            double p1_acc = start_position1.Coordinate.Accuracy;
            double p2_acc = start_position2.Coordinate.Accuracy;
            double p3_acc = start_position3.Coordinate.Accuracy;

            avg_start_point_accuracy = (p1_acc + p2_acc + p3_acc) / 3;

            double total_accuracy_error = p1_acc + p2_acc + p3_acc;
            double p1_temp = total_accuracy_error / p1_acc;
            double p2_temp = total_accuracy_error / p2_acc;
            double p3_temp = total_accuracy_error / p3_acc;

            double p123temp = p1_temp + p2_temp + p3_temp;

            double p1_factor = total_accuracy_error / p1_acc / p123temp;
            double p2_factor = total_accuracy_error / p2_acc / p123temp;
            double p3_factor = total_accuracy_error / p3_acc / p123temp;

            double avg_latitude = (p1_factor * start_position1.Coordinate.Point.Position.Latitude +
                                   p2_factor * start_position2.Coordinate.Point.Position.Latitude +
                                   p3_factor * start_position3.Coordinate.Point.Position.Latitude);

            double avg_longitude = (p1_factor * start_position1.Coordinate.Point.Position.Longitude +
                                    p2_factor * start_position2.Coordinate.Point.Position.Longitude +
                                    p3_factor * start_position3.Coordinate.Point.Position.Longitude);

            double avg_altitude = (p1_factor * start_position1.Coordinate.Point.Position.Altitude +
                                   p2_factor * start_position2.Coordinate.Point.Position.Altitude +
                                   p3_factor * start_position3.Coordinate.Point.Position.Altitude);

            avg_start_position.Latitude = avg_latitude;
            avg_start_position.Longitude = avg_longitude;
            avg_start_position.Altitude = avg_altitude;

            Debug.WriteLine("p1_factor: " + p1_factor + "p2_factor: " + p2_factor + "p3_factor: " + p3_factor);

            Debug.WriteLine("avg_start_point_accuracy: " + avg_start_point_accuracy);
        }


        private string GetStatusString(PositionStatus status)
        {
            var strStatus = "";
            switch (status)
            {
                case PositionStatus.Ready:
                    strStatus = "Measuring";
                    break;

                case PositionStatus.Initializing:
                    strStatus = "Getting your location, please wait...";
                    break;

                case PositionStatus.NoData:
                    strStatus = "Location data is not available.";
                    break;

                case PositionStatus.Disabled:
                    strStatus = "Location services are disabled.\n" +
                                "Enable them from your device's settings.";
                    break;

                case PositionStatus.NotInitialized:
                    strStatus = "Getting your location...";
                    break;

                case PositionStatus.NotAvailable:
                    strStatus = "Location services are not supported on your device.";
                    break;

                default:
                    strStatus = "An error occurred.\nPlease try to close and restart the application.";
                    break;
            }
            return (strStatus);
        }



        #region setgeolocatorstates

        // use this when navigated to the page but no measuring is done
        private void setGeolocatorToInitState(string toStatusLabel, bool showProgressBar = true, bool clearStatusAfterInit = false)
        {
            setStartBtn.IsEnabled = false;
            if (clearStatusAfterInit)
                clearStatusMsg = true;
            if (showProgressBar)
                progressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
            else
                progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            statusLbl.Text = toStatusLabel;

            // set these values only once, because they cannot be changed 
            // while getting location or being subscribed to the Geolocator's events SMART!
            if (!isGeolocatorInitialized)
            {
                geolocator.MovementThreshold = MOVEMENT_TRESHOLD;
                geolocator.DesiredAccuracyInMeters = DESIRED_ACCURACY;
                geolocator.ReportInterval = REPORT_INTERVAL_START;
                isGeolocatorInitialized = true;
            }

            geolocator.StatusChanged += geolocator_StatusChanged;
            geolocator.PositionChanged += geolocator_init;

            // if locator is already in ready state, set button enabled
            if (geolocator.LocationStatus == PositionStatus.Ready)
            {
                setStartBtn.IsEnabled = true;
                progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }
        }
        // use this when navigated to the page but no measuring is done
        private void setGeolocatorToGetLocationsState()
        {
            setStartBtn.IsEnabled = false;
            progressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
            start_position_set = false;
            start_position_count = 0;
            resultLbl_m.Text = "0";
            resultLbl_ft.Text = "0";
            statusLbl.Text = STARTING_GPS;
            accuracyTxt.Text = "";
            accuracyLbl.Text = "";
            detachGeolocator();
        }


        // use this when all is set and measuring
        private void setGeolocatorToReportState()
        {
            start_position_set = true;
            //setStartBtn.IsEnabled = true;

            if (!isGeolocatorInitialized)
            {
                geolocator.DesiredAccuracyInMeters = GOOD_ACCURACY_IN_METERS;
                geolocator.ReportInterval = REPORT_INTERVAL_RUNTIME;
                geolocator.MovementThreshold = MOVEMENT_TRESHOLD;
                isGeolocatorInitialized = true;
            }
            geolocator.PositionChanged += geolocator_PositionChanged;
            //progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        // use this when suspending or navigating away
        private void detachGeolocator()
        {
            geolocator.PositionChanged -= geolocator_PositionChanged;
            geolocator.PositionChanged -= geolocator_init;
            geolocator.StatusChanged -= geolocator_StatusChanged;
        }



        #endregion

        /// <summary>
        /// Populates the page with content passed during navigation.  Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="sender">
        /// The source of the event; typically <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">Event data that provides both the navigation parameter passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested and
        /// a dictionary of state preserved by this page during an earlier
        /// session.  The state will be null the first time a page is visited.</param>
        private async void NavigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
            Debug.WriteLine("LoadState");
            // If a hardware Back button is present, hide the "soft" Back button
            // in the command bar, and register a handler for presses of the hardware
            if (Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.Phone.UI.Input.HardwareButtons"))
            {
                HardwareButtons.BackPressed += HardwareButtons_BackPressed;
                Debug.WriteLine("HWButtonTypeIsPresent");
            }

            //var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            GeolocationAccessStatus status = await Geolocator.RequestAccessAsync();
            if (status == GeolocationAccessStatus.Allowed)
            {
                settingsLink.Visibility = Visibility.Collapsed;
                bool goToInitState = false;
                //if a saved state is found, read the values and update the state
                if (e.PageState != null)
                {
                    if (e.PageState.ContainsKey("avg_start_position_lat") && (double)e.PageState["avg_start_position_lat"] != 0
                        && e.PageState.ContainsKey("avg_start_position_lon") && (double)e.PageState["avg_start_position_lon"] != 0
                        && e.PageState.ContainsKey("avg_start_position_alt") && e.PageState.ContainsKey("avg_start_position_accuracy")
                        && e.PageState.ContainsKey("result_m") && e.PageState.ContainsKey("result_ft"))
                    {
                        // check if start position is too old
                        if (e.PageState.ContainsKey("start_position_timestamp"))
                        {
                            DateTime oldTime = new DateTime();
                            bool parseOK = DateTime.TryParse((string)e.PageState["start_position_timestamp"], out oldTime);

                            // if parse succesful, set timestamp, if not, set it to now
                            if (parseOK)
                            {
                                start_position_timestamp = oldTime;
                            }
                            else
                            {
                                start_position_timestamp = DateTime.Now;
                            }

                            long difference = DateTime.Now.Ticks - oldTime.Ticks;
                            TimeSpan differenceSpan = new TimeSpan(difference);
                            Debug.WriteLine("difference: " + differenceSpan.TotalHours + "h " + differenceSpan.TotalMinutes + "min " + differenceSpan.TotalSeconds + "sec");

                            if (differenceSpan.TotalHours < MAX_START_POSITION_AGE_IN_HOURS)
                            {
                                Debug.WriteLine("start_position not too old");

                                avg_start_position.Latitude = (double)e.PageState["avg_start_position_lat"];
                                avg_start_position.Longitude = (double)e.PageState["avg_start_position_lon"];
                                avg_start_position.Altitude = (double)e.PageState["avg_start_position_alt"];
                                avg_start_point_accuracy = (double)e.PageState["avg_start_position_accuracy"];
                                result_m = (double)e.PageState["result_m"];
                                result_ft = (double)e.PageState["result_ft"];


                                Debug.WriteLine("page state restored");
                                Debug.WriteLine("LoadState - result_m: " + result_m + " result_ft: " + result_ft);
                                progressBar.Visibility = Windows.UI.Xaml.Visibility.Visible;
                                statusLbl.Text = RESUMING;
                                setStartBtn.IsEnabled = false;
                                setGeolocatorToReportState();
                            }
                            else
                            {
                                // start position was too old -> 
                                goToInitState = true;
                            }
                        }
                    }
                    else
                    {
                        goToInitState = true;
                        // avg start position is zero
                        //Debug.WriteLine("start position was zero");
                    }
                }
                // no saved state, launching first time
                else
                {
                    goToInitState = true;
                    Debug.WriteLine("no saved state, to init state");

                }
                if (goToInitState)
                {
                    setGeolocatorToInitState(STARTING_GPS, true, true);
                }
                setLabels();

            }
            else
            {
                Debug.WriteLine("no location consent");
    
                // The app is not allowed to location
                setStartBtn.IsEnabled = false;
                statusLbl.Text = LOCATION_OPTED_OUT;
                settingsLink.Visibility = Visibility.Visible;
                //var msg = new MessageDialog(LOCATION_CONSENT_QUESTION);
                //var okBtn = new UICommand("yes", new UICommandInvokedHandler(CommandHandler));
                //var cancelBtn = new UICommand("no", new UICommandInvokedHandler(CommandHandler));
                //msg.Commands.Add(okBtn);
                //msg.Commands.Add(cancelBtn);
                //IUICommand result = await msg.ShowAsync();
            }
        }

        void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            Application.Current.Exit();
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="sender">The source of the event; typically <see cref="NavigationHelper"/></param>
        /// <param name="e">Event data that provides an empty dictionary to be populated with
        /// serializable state.</param>
        private void NavigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            e.PageState.Add("avg_start_position_lat", avg_start_position.Latitude);
            e.PageState.Add("avg_start_position_lon", avg_start_position.Longitude);
            e.PageState.Add("avg_start_position_alt", avg_start_position.Altitude);
            e.PageState.Add("avg_start_position_accuracy", avg_start_point_accuracy);
            e.PageState.Add("result_m", result_m);
            e.PageState.Add("result_ft", result_ft);
            e.PageState.Add("start_position_timestamp", start_position_timestamp.ToString());

            //Debug.WriteLine("SaveState - result_m: " + result_m + " result_ft: " + result_ft);

            detachGeolocator();
        }

        #region NavigationHelper registration

        /// <summary>
        /// The methods provided in this section are simply used to allow
        /// NavigationHelper to respond to the page's navigation methods.
        /// <para>
        /// Page specific logic should be placed in event handlers for the  
        /// <see cref="NavigationHelper.LoadState"/>
        /// and <see cref="NavigationHelper.SaveState"/>.
        /// The navigation parameter is available in the LoadState method 
        /// in addition to page state preserved during an earlier session.
        /// </para>
        /// </summary>
        /// <param name="e">Provides data for navigation methods and event
        /// handlers that cannot cancel the navigation request.</param>

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Debug.WriteLine("OnNavigatedTo");
            this.navigationHelper.OnNavigatedTo(e);
        }

        private async void geolocator_StatusChanged(Geolocator sender, StatusChangedEventArgs args)
        {
            /*if (args.Status == PositionStatus.Initializing)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    this.statusLbl.Text = STARTING_GPS;
                    progressBar.Visibility = Windows.UI.Xaml.Visibility.Visible; 
                });

            }*/
            if (args.Status == PositionStatus.Ready)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    this.setStartBtn.IsEnabled = true;
                    progressBar.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                    if (clearStatusMsg)
                    {
                        this.statusLbl.Text = "";
                        clearStatusMsg = false;
                    }
                });
            }
        }

        private async void geolocator_init(Geolocator sender, PositionChangedEventArgs args)
        {
            Debug.WriteLine("init - report interval: " + sender.ReportInterval);
            
            Debug.WriteLine("locator_init status:" + geolocator.LocationStatus.ToString()
                + "accuracy:" + args.Position.Coordinate.Accuracy.ToString());

            //double total_avg_accuracy = args.Position.Coordinate.Accuracy;
            if (args.Position.Coordinate.Accuracy <= GOOD_ACCURACY_IN_METERS)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    accuracyLbl.Text = GOOD_ACCURACY;
                    accuracyLbl.Foreground = new SolidColorBrush(Windows.UI.Colors.LimeGreen);
                    accuracyTxt.Text = GPS_ACCURACY_TXT;
                });
            }
            else if (args.Position.Coordinate.Accuracy <= MEDIUM_ACCURACY_IN_METERS)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    accuracyLbl.Text = MEDIUM_ACCURACY;
                    accuracyLbl.Foreground = new SolidColorBrush(Windows.UI.Colors.Orange);
                    accuracyTxt.Text = GPS_ACCURACY_TXT;
                });
            }
            else
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    accuracyLbl.Text = POOR_ACCURACY;
                    accuracyLbl.Foreground = new SolidColorBrush(Windows.UI.Colors.Red);
                    accuracyTxt.Text = GPS_ACCURACY_TXT;
                });
            }

        }
        /*
                private void CommandHandler(IUICommand command)
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

                    if( command.Label == "yes")
                    {
                        localSettings.Values["LocationConsent"] = true;
                        setGeolocatorToInitState(STARTING_GPS,true,true);
                    }
                    else
                    {
                        localSettings.Values["LocationConsent"] = false;
                        setStartBtn.IsEnabled = false;
                        statusLbl.Text = LOCATION_OPTED_OUT;
                    }
                }
                */

        private void setLabels()
        {
            //set current result
            if (result_m >= MIN_DISTANCE_M)
            {
                resultLbl_m.Text = result_m.ToString();
                resultLbl_ft.Text = result_ft.ToString();
            }
            else
            {
                resultLbl_m.Text = "0";
                resultLbl_ft.Text = "0";
            }

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            string unit = "METERS";
            if (localSettings.Values.ContainsKey("displayUnit"))
            {
                unit = (string)localSettings.Values["DisplayUnit"];
            }

            if (unit == "METERS")
            {
                resultLbl_m.Visibility = Windows.UI.Xaml.Visibility.Visible;
                resultLbl_ft.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                unitLbl_m.Visibility = Windows.UI.Xaml.Visibility.Visible;
                unitLbl_ft.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }
            else if (unit == "FEET")
            {
                resultLbl_m.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                resultLbl_ft.Visibility = Windows.UI.Xaml.Visibility.Visible;
                unitLbl_m.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                unitLbl_ft.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
            else if (unit == "METERES_AND_FEET")
            {
                //todo both
            }
        }
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            this.navigationHelper.OnNavigatedFrom(e);
        }

        #endregion

        private void AppBarSettings_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(SettingsPage));
        }

        private void AppBarAbout_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(AboutPage));
        }
    }
}
