using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core;

namespace uTorrentCleanup
{
    internal static class Program
    {
        private static readonly Regex TokenRegEx = new Regex("<div id='token' style='display:none;'>([^<]*)</div>", RegexOptions.Compiled);
        private static readonly string Username = ConfigurationManager.AppSettings["Username"];
        private static readonly string Password = ConfigurationManager.AppSettings["Password"];
        private static readonly string Url = ConfigurationManager.AppSettings["Url"];

        private static Logger log;

        private static void Main(string[] args)
        {
            // Initialize logging
            log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("log.txt")
                .CreateLogger();

            string[] strArray = ConfigurationManager.AppSettings["Args"].Split(' ');

            // %I - hex encoded info-hash
            int argsIndex1 = GetArgsIndex(strArray, "%I");

            // %N - Title of torrent
            int argsIndex2 = GetArgsIndex(strArray, "%N");

            // %S - State of torrent
            int argsIndex3 = GetArgsIndex(strArray, "%S");

            bool checkAll = args.Any(a => a.ToLowerInvariant().Trim() == "--checkall");

            // States
            //Error - 1
            //Checked - 2
            //Paused - 3
            //Super seeding -4
            //Seeding - 5
            //Downloading - 6
            //Super seed[F] -7
            //Seeding[F] - 8
            //Downloading[F] - 9
            //Queued seed -10
            //Finished - 11
            //Queued - 12
            //Stopped - 13
            //Queued - 12
            //Pre-allocating - 17
            //Downloading Metadata -18
            //Connecting to Peers - 19
            //Moving - 20
            //Flushing - 21
            //Need DHT -22
            //Finding Peers -23
            //Resolving - 24
            //Writing - 25

            string hash = args[argsIndex1];
            string torrentName = args[argsIndex2];
            string torrentStatus = args[argsIndex3];

            CheckTorrent(hash, torrentName, torrentStatus, checkAll);

            Thread.Sleep(3000);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputSplit"></param>
        /// <param name="match"></param>
        /// <returns></returns>
        private static int GetArgsIndex(IReadOnlyList<string> inputSplit, string match)
        {
            for (int index = 0; index < inputSplit.Count; ++index)
            {
                if (inputSplit[index] == match)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 
        /// </summary>
        private static void CheckTorrent(string hash, string torrentName, string torrentStatus, bool checkAll = false)
        {
            using (WebClient webClient = new WebClient { Credentials = new NetworkCredential(Username, Password) })
            {
                // Get token
                string tokenData = webClient.DownloadString(Url + "/token.html");
                Match tokenMatch = TokenRegEx.Match(tokenData);

                if (!tokenMatch.Success)
                {
                    log.Error("Unable to get access token.");
                    return;
                }

                string accessToken = tokenMatch.Groups[1].Value;
                string accessCookie = webClient.ResponseHeaders["Set-Cookie"];

                webClient.Headers.Add(HttpRequestHeader.Cookie, accessCookie);

                if (!checkAll)
                {
                    switch (torrentStatus)
                    {
                        case "1":
                        case "3":
                        case "13":
                            webClient.DownloadString(Url + "/?action=start&hash=" + hash + $"&token={accessToken}");
                            log.Information(torrentName + " (re)starting..");
                            break;
                        case "8":
                            webClient.DownloadString(Url + "/?action=stop&hash=" + hash + $"&token={accessToken}");
                            log.Information(torrentName + " stopped..");
                            break;
                        case "11":
                            webClient.DownloadString(Url + "/?action=remove&hash=" + hash + $"&token={accessToken}");
                            log.Information(torrentName + " removed..");
                            break;
                        default:
                            log.Information(torrentName + " ignored..");
                            break;
                    }
                    return;
                }

                log.Information("Checking all torrents.");

                // Torrent URL
                string torrentUrl = Url + $"/?list=1&token={accessToken}";

                // Download torrent json
                string torrentJson = webClient.DownloadString(torrentUrl);

                foreach (JToken token in JObject.Parse(torrentJson)["torrents"])
                {
                    accessCookie = webClient.ResponseHeaders["Set-Cookie"];
                    webClient.Headers.Add(HttpRequestHeader.Cookie, accessCookie);

                    string str1 = token[0].ToString();
                    string name = token[2].ToString();
                    string lower = token[21].ToString().ToLower();

                    if (lower.StartsWith("stopped") || lower.StartsWith("error"))
                    {
                        webClient.DownloadString(Url + "/?action=start&hash=" + str1 + $"&token={accessToken}");
                        log.Information(name + " (re)starting..");
                    }
                    else if (lower.StartsWith("seed"))
                    {
                        webClient.DownloadString(Url + "/?action=stop&hash=" + str1 + $"&token={accessToken}");
                        log.Information(name + " stopped..");
                    }
                    else if (lower.StartsWith("completed") || lower.StartsWith("finished"))
                    {
                        webClient.DownloadString(Url + "/?action=remove&hash=" + str1 + $"&token={accessToken}");
                        log.Information(name + " removed..");
                    }
                    else
                    {
                        log.Information(name + " ignored..");
                    }
                }
            }
        }
    }
}