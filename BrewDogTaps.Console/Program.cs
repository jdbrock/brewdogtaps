using BrewDogTaps.Database;
using NLog;
using PushoverClient;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Core.Credentials;

namespace BrewDogTaps
{
    public class Program
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        private const String DEVICE_ID = "GE0ESkZfSVoZIDZXNCdeSidbWRtJUV9XHE9KI0VUQlVSWFwbJVZeLGtTMEdbRgIIBwoBDF5HAj9GDRwAVUhNCBBNVwwNNQMAQAdSKiREV0lXQVwMSEcFDkMHUF9VLgACQRI=";
        private const String BASE_URL = "http://app.brewdog.com/api/";

        private static String AccessToken { get; set; }
        private static RestClient Client { get; set; }

        private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);

        public static void Main(string[] args)
        {
            _logger.Trace("Tap Hub - Brewdog Integration v0.2");
            _logger.Trace("----------------------------------");

            EnsureTwitterAuth();

            Client = new RestClient();

            Client.BaseUrl = new Uri(BASE_URL);

            Client.AddDefaultHeader("X-App-Bundle", "6");
            Client.AddDefaultHeader("User-Agent", "BrewDog/6 CFNetwork/711.5.6 Darwin/14.0.0");
            Client.AddDefaultHeader("Accept-Language", "en-gb");
            Client.AddDefaultHeader("Content-Type", "application/json; charset=utf-8");

            var ti = CultureInfo.CurrentCulture.TextInfo;

            while (true)
            {
                var db = new TapHubEntities();

                _logger.Debug("Connecting to the Brewdog API...");

                var data = FetchBarData();
                var noChanges = true;

                if (data != null)
                {
                    foreach (var apiBar in data.bars)
                    {
                        // Generate the full BrewDog bar name.
                        var barName = $"BrewDog {ti.ToTitleCase(apiBar.name)}";

                        // Create bar if it doesn't already exist.
                        var dbBar = db.Bars.FirstOrDefault(X => X.Name == barName);
                        if (dbBar == null)
                        {
                            dbBar = new Database.Bar
                            {
                                Id = Guid.NewGuid(),
                                Name = barName
                            };

                            db.Bars.Add(dbBar);
                            db.SaveChanges();
                        }

                        var dbTaps = dbBar.Taps
                            .ToDictionary(X => X.Name);

                        var apiTaps = apiBar.data.tap
                            .Where(FilterDummyTaps)
                            .Distinct(new TapNameEqualityComparer())
                            .ToDictionary(X => X.name);

                        var goneOff = dbTaps.Keys.Except(apiTaps.Keys);
                        var goneOn = apiTaps.Keys.Except(dbTaps.Keys);

                        var barNamePretty = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(barName);

                        if (goneOff.Any() || goneOn.Any())
                        {
                            _logger.Debug($"{barNamePretty}: {apiTaps.Count} beers found. {goneOff.Count()} have gone off, {goneOn.Count()} have gone on.");
                            noChanges = false;
                        }
                        else
                            _logger.Trace($"{barNamePretty}: {apiTaps.Count} beers found. {goneOff.Count()} have gone off, {goneOn.Count()} have gone on.");

                        if (!goneOff.Any() && !goneOn.Any())
                            continue; // Skip to the next bar.

                        // Create messages.
                        var messages = new List<String>();

                        foreach (var key in goneOff)
                            messages.Add($"OFF: {dbTaps[key].Name}");

                        foreach (var key in goneOn)
                            messages.Add($"ON: {apiTaps[key].name}");

                        // Log.
                        foreach (var message in messages)
                            _logger.Debug(message);

                        // Tweet about it.
                        foreach (var message in messages)
                            PostTweet(message, barName);

                        // Something has changed, update the database.
                        foreach (var key in goneOff)
                            db.Taps.Remove(dbTaps[key]);

                        foreach (var key in goneOn)
                        {
                            var apiTap = apiTaps[key];

                            db.Taps.Add(new Database.Tap
                            {
                                Id = Guid.NewGuid(),
                                FirstSeen = DateTime.UtcNow,
                                Bar = dbBar,
                                Name = apiTap.name
                            });
                        }

                        // Save database changes.
                        db.SaveChanges();

                        //// Try to raise notifications.
                        //foreach (var message in messages)
                        //    try
                        //    {
                        //        SendNotification(message, barName);
                        //    }
                        //    catch { }
                    }
                }

                if (noChanges)
                    _logger.Debug("No tap changes.");

                _logger.Debug($"Sleeping for {_refreshInterval}...");
                Thread.Sleep(_refreshInterval);
            }
        }

        //private static void SendNotification(String message, String barName)
        //{
        //    if (barName != "BrewDog EDINBURGH")
        //        return;

        //    var userKey = "";
        //    SendNotificationCore("Tap Hub", message, userKey);
        //}

        //private static void SendNotificationCore(String title, String message, String userKey)
        //{
        //    var client = new Pushover("");
        //    client.Push(title, message, userKey);
        //}

        private static void PostTweet(String message, String barName)
        {
            if (barName != "BrewDog EDINBURGH")
                return;

            Tweet.PublishTweet(message);
        }

        private static void EnsureTwitterAuth()
        {
            var consumerKey = ConfigurationManager.AppSettings["TWITTER_CONSUMER_KEY"];
            var consumerSecret = ConfigurationManager.AppSettings["TWITTER_CONSUMER_SECRET"];

            //if (false)
            //{
            //    // Create a new set of credentials for the application
            //    var appCredentials = new TwitterCredentials(consumerKey, consumerSecret);

            //    // Go to the URL so that Twitter authenticates the user and gives him a PIN code
            //    var url = CredentialsCreator.GetAuthorizationURL(appCredentials);

            //    // This line is an example, on how to make the user go on the URL
            //    Process.Start(url);

            //    // Ask the user to enter the pin code given by Twitter
            //    Console.WriteLine("Setting up Twitter...");
            //    Console.WriteLine("Enter Twitter PIN: ");
            //    var pinCode = Console.ReadLine();
            //    Console.WriteLine();

            //    // With this pin code it is now possible to get the credentials back from Twitter
            //    var userCredentials = CredentialsCreator.GetCredentialsFromVerifierCode(pinCode, appCredentials);

            //    // Use the user credentials in your application
            //    Auth.SetCredentials(userCredentials);
            //}
            //else
            //{
                var accessToken = ConfigurationManager.AppSettings["TWITTER_ACCESS_TOKEN"];
                var accessTokenSecret = ConfigurationManager.AppSettings["TWITTER_ACCESS_TOKEN_SECRET"];


                Auth.SetCredentials(new TwitterCredentials(consumerKey, consumerSecret, accessToken, accessTokenSecret));
            //}
        }

        private static bool FilterDummyTaps(Tap arg)
        {
            return
                !String.IsNullOrWhiteSpace(arg.name) &&
                !arg.name.Contains("DRAFT") &&
                !arg.name.Contains("HOPINATOR") &&
                !arg.name.Contains("In The Hopinator") &&
                !arg.name.Contains("HOP CANNON") &&
                !arg.name.Contains("CIDER") &&
                !arg.name.Contains("KEG") &&
                !arg.name.Contains("GUEST") &&
                !arg.name.Contains("Guest Beer") &&
                !arg.name.Contains("GUEST BEER") &&
                !arg.name.Contains("Brewdog Beer");
        }

        private static BarDataResponse FetchBarData()
        {
            try
            {
                EnsureToken();

                var request = new RestRequest("bars.json", Method.POST);
                request.AddHeader("X-App-Token", AccessToken);

                var response = Client.Execute<BarDataResponse>(request);

                if (response.StatusCode != HttpStatusCode.OK)
                    throw new ApplicationException($"Error. Received Status Code: {response.StatusCode}");

                return response.Data;
            }
            catch (Exception e)
            {
                _logger.Debug("Error: {0}", e);
                return null;
            }
        }

        private static void EnsureToken()
        {
            var request = new RestRequest("device.json", Method.POST);
            request.AddJsonBody(new AccessTokenRequest { id = DEVICE_ID });

            var response = Client.Execute<AccessTokenResponse>(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new ApplicationException($"Error. Received Status Code: {response.StatusCode}");

            AccessToken = response.Data.token;
        }

        private class TapNameEqualityComparer : IEqualityComparer<Tap>
        {
            public bool Equals(Tap x, Tap y)
            {
                if (x == null || y == null)
                {
                    if (x == y)
                        return true;

                    return false;
                }

                return x.name == y.name;
            }

            public int GetHashCode(Tap obj)
            {
                if (obj == null || obj.name == null)
                    return 0;

                return obj.name.GetHashCode();
            }
        }
    }
}
