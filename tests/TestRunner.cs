using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using PlaylistFlac;

internal static class TestRunner
{
    private sealed class Result { internal string Name, Error; internal double Seconds; }
    [STAThread]
    private static int Main()
    {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string engine = Path.Combine(root, "engine", "sockseek.exe");
        var suites = new Dictionary<string, Action> {
            {"WindowLayout",WindowLayoutTests.Run}, {"Import",ImportTests.Run}, {"SearchNames",SearchNameTests.Run},
            {"Engine",EngineTests.Run}, {"Audit",AuditTests.Run}, {"RecordingIdentity",RecordingIdentityTests.Run},
            {"FlacIdentity",FlacIdentityTests.Run}, {"FlacArtwork",FlacArtworkTests.Run}, {"CoverLookup",CoverLookupTests.Run},
            {"ArtworkLibrary",ArtworkLibraryTests.Run}, {"ArtworkPerformance",ArtworkPerformanceTests.Run},
            {"DuplicateLibrary",DuplicateLibraryTests.Run}, {"Naming",NamingTests.Run}, {"Smart",SmartTests.Run},
            {"AliasLookup",AliasLookupTests.Run}, {"LibraryStatus",LibraryStatusTests.Run}, {"ProgressState",ProgressStateTests.Run},
            {"RetryList",RetryListTests.Run}, {"RecoveryFolders",RecoveryFolderTests.Run}, {"TransferTelemetry",TransferTelemetryTests.Run},
            {"RecordingCatalog",RecordingCatalogTests.Run}, {"RetryReview",RetryReviewTests.Run},
            {"LibraryLayout",LibraryLayoutTests.Run}, {"TrackFileActions",TrackFileActionsTests.Run},
            {"RealEngine",delegate { EngineTests.CheckRealEngine(engine); }},
            {"ParallelDownloads",delegate { ParallelDownloadTests.Run(engine); }},
            {"LocalSettings",LocalSettings}, {"InputProperties",InputPropertyTests.Run}, {"Environment",EnvironmentTests.Run},
            {"MissingTrackExport",MissingTrackExportTests.Run}
        };
        var results = new List<Result>(); DateTime started = DateTime.UtcNow;
        foreach (var suite in suites)
        {
            var clock = Stopwatch.StartNew(); var result = new Result { Name = suite.Key };
            try { suite.Value(); }
            catch (Exception ex) { result.Error = ex.ToString(); }
            result.Seconds = clock.Elapsed.TotalSeconds; results.Add(result);
            Console.WriteLine((result.Error == null ? "PASS " : "FAIL ") + result.Name + " " + result.Seconds.ToString("0.000",CultureInfo.InvariantCulture) + "s");
        }
        int failed = results.Count(x => x.Error != null);
        using (var xml = XmlWriter.Create(Path.Combine(root,"test-results.xml"),new XmlWriterSettings { Indent=true, Encoding=new UTF8Encoding(false) }))
        {
            xml.WriteStartElement("testsuites"); xml.WriteStartElement("testsuite");
            xml.WriteAttributeString("name","FLAC-Downloader offline suites");
            xml.WriteAttributeString("tests",results.Count.ToString()); xml.WriteAttributeString("failures",failed.ToString());
            xml.WriteAttributeString("errors","0"); xml.WriteAttributeString("skipped","0");
            xml.WriteAttributeString("timestamp",started.ToString("o"));
            xml.WriteAttributeString("time",(DateTime.UtcNow-started).TotalSeconds.ToString("0.000",CultureInfo.InvariantCulture));
            xml.WriteStartElement("properties");
            Property(xml,"count-unit","suite (each suite contains multiple assertions)");
            Property(xml,"app-sha256",Hash(typeof(Track).Assembly.Location));
            Property(xml,"engine-sha256",Hash(engine));
            Property(xml,"runner-sha256",Hash(typeof(TestRunner).Assembly.Location));
            Property(xml,"revision",Environment.GetEnvironmentVariable("FLAC_TEST_REVISION") ?? "unknown");
            Property(xml,"working-tree",Environment.GetEnvironmentVariable("FLAC_TEST_TREE") ?? "unknown");
            xml.WriteEndElement();
            foreach (var result in results)
            {
                xml.WriteStartElement("testcase"); xml.WriteAttributeString("name",result.Name); xml.WriteAttributeString("classname","OfflineSuite");
                xml.WriteAttributeString("time",result.Seconds.ToString("0.000",CultureInfo.InvariantCulture));
                if(result.Error != null) { xml.WriteStartElement("failure"); xml.WriteString(result.Error); xml.WriteEndElement(); }
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); xml.WriteEndElement();
        }
        File.WriteAllText(Path.Combine(root,"test-results.txt"),(failed==0 ? "PASS: " : "FAIL: ")+results.Count+" suites, "+failed+" failed, 0 skipped; "+started.ToString("o")+"\r\n"+String.Join("\r\n",results.Select(x=>x.Name+": "+(x.Error ?? "passed")))+"\r\n");
        File.WriteAllText(Path.Combine(root,"artwork-performance.txt"),ArtworkPerformanceTests.Result+"\r\n");
        return failed==0 ? 0 : 1;
    }
    private static void Property(XmlWriter xml,string name,string value)
    { xml.WriteStartElement("property"); xml.WriteAttributeString("name",name); xml.WriteAttributeString("value",value); xml.WriteEndElement(); }
    private static string Hash(string path)
    { using(var hash=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(file)).Replace("-",""); }
    private static void LocalSettings()
    {
        string p=AppState.PlaylistFolder("C:\\Music","..\\CON: / unsafe","one");
        if(!p.StartsWith("C:\\Music\\") || p.Contains("..\\"))throw new Exception("Unsafe playlist path");
        if(p==AppState.PlaylistFolder("C:\\Music","..\\CON: / unsafe","two"))throw new Exception("Playlist identity collision");
        string secret="Unicode password \u266b = spaces",plain;
        if(!AppState.TryUnprotect(AppState.Protect(secret),out plain) || plain!=secret)throw new Exception("Credential round trip failed");
        if(AppState.TryUnprotect("invalid-base64",out plain) || plain!="")throw new Exception("Damaged saved password accepted");
        if(AppState.TryUnprotect(Convert.ToBase64String(new byte[64]),out plain) || plain!="")throw new Exception("Foreign encrypted password accepted");
        var serializer=new JavaScriptSerializer();
        var old=serializer.Deserialize<Preferences>("{\"StrictMatch\":true}");
        if(old.ParallelTracks!=20 || !old.StrictMatch)throw new Exception("Settings migration failed");
        var saved=serializer.Deserialize<Preferences>(serializer.Serialize(new Preferences{ParallelTracks=32}));
        if(saved.ParallelTracks!=32)throw new Exception("Settings round trip failed");
        if(typeof(Track).Assembly.GetTypes().Any(t=>t.Name.EndsWith("Tests") || t.Name=="TestRunner"))throw new Exception("Test types leaked into the production assembly");
    }
}
