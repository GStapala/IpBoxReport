using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Atlassian.Jira;
using Atlassian.Jira.Remote;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using Octokit;
using OfficeOpenXml;
using ProductHeaderValue = System.Net.Http.Headers.ProductHeaderValue;

namespace IpBoxReport
{


    class Program
    {
        private static Dictionary<int, int> map = new Dictionary<int, int>
        {
            { 1,176},
            { 2,160},
            { 3,168},
            { 4,176},
            { 5,168},
            { 6,148},
            { 7,184},
            { 8,120},
            { 9,168},
            { 10,176},
            { 11,168},
            { 12,176},
        };

        static void Main(string[] args)
        {
            GenerateGithubReport();
            SplitToMonths();
        }

        public static DataTable ToDataTable(IEnumerable<dynamic> items)
        {
            var data = items.ToArray();
            if (data.Count() == 0) return null;

            var dt = new DataTable();
            foreach (var key in ((IDictionary<string, object>)data[0]).Keys)
            {
                dt.Columns.Add(key);
            }

            foreach (var d in data)
            {
                dt.Rows.Add(((IDictionary<string, object>)d).Values.ToArray());
            }

            return dt;
        }

        private static void SplitToMonths()
        {
            var currentDirectory = Directory.GetCurrentDirectory() + "report.csv";
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
            using (var reader = new StreamReader(currentDirectory))
            using (var csv = new CsvReader(reader, config))
            {
                var records = csv.GetRecords<Result>();

                var groupedByMonth = records.GroupBy(result => new { result.DateTime.Month }).ToList();
                var jira = Jira.CreateRestClient("https://jira.eg.dk/", "", "");
                foreach (var month in groupedByMonth)
                {
                    var result = new List<Result2>();
                    string monthname =
                        new DateTime(2020, month.Key.Month, 1).ToString("MMMM",
                            new System.Globalization.CultureInfo("pl-PL"));


                    foreach (var r in month.GroupBy(result1 => new { result1.DateTime.Day, result1.Project }))
                    {
                        string dayname = new DateTime(2020, month.Key.Month, r.Key.Day).ToString("dddd",
                            new System.Globalization.CultureInfo("pl-PL"));

                        StringBuilder jiraDescription = new StringBuilder();
                        var descriptions = new HashSet<string>();
                        foreach (var result1 in r)
                        {
                            var regex = new Regex(@"XNA-[0-9].*");
                            // return regex.Matches(strInput);
                            if (regex.IsMatch(result1.Branch))
                            {
                                var asd = regex.Match(result1.Branch);

                                try
                                {
                                    var issueKey = asd.Value;
                                    if (descriptions.Contains(issueKey))
                                        continue;

                                    descriptions.Add(issueKey);
                                    var issue = jira.Issues.GetIssueAsync(issueKey).Result;

                                    jiraDescription.Insert(0,
                                        $" {issue.Key} - Title: {issue.Summary} {Environment.NewLine} Description: {issue.Description} {Environment.NewLine} Link: https://jira.eg.dk/browse/{issueKey}  {Environment.NewLine}");
                                    while (issue.ParentIssueKey != null)
                                    {
                                        jiraDescription.Insert(0, $"Parent of: {issue.Key}");
                                        issue = jira.Issues.GetIssueAsync(issue.ParentIssueKey).Result;
                                        jiraDescription.Insert(0,
                                            $" {issue.Key} - Title: {issue.Summary} {Environment.NewLine} Description: {issue.Description} {Environment.NewLine} Link: https://jira.eg.dk/browse/{issueKey}  {Environment.NewLine}");

                                    }

                                    descriptions.Add(issueKey);
                                }
                                catch (Exception e)
                                {

                                }
                            }
                        }



                        result.Add(new Result2
                        {

                            DateTime = $"{dayname} -- {r.First().DateTime.ToString()}",
                            Comment = string.Join(Environment.NewLine, r.Select(e => e.Comment)),
                            Project = string.Join(Environment.NewLine, r.Select(e => e.Project)),
                            Branch = string.Join(Environment.NewLine, r.Select(e => e.Branch)),
                            PrUrl = string.Join(Environment.NewLine,
                                r.Select(e => e.PrUrl.Replace("https://api.github.com/repos/", "https://github.com/"))),
                            JiraDescription = jiraDescription.ToString()
                        });
                    }

                    var _memoryStream = new MemoryStream();
                    var _streamWriter = new StreamWriter(_memoryStream);
                    var _csvWriter = new CsvWriter(_streamWriter,
                        new CsvConfiguration(new System.Globalization.CultureInfo("pl-PL")) { Delimiter = ";" });

                    var random = new Random();

                    foreach (var result2s in result.GroupBy(result2 => DateTime.Parse(result2.DateTime.Split("--")[1]).Day))
                    {
                        var hours = random.Next(5, 11);
                        result2s.First().Hours = hours;
                    }

                    _csvWriter.WriteRecordsAsync(
                        result.OrderBy(result => DateTime.Parse(result.DateTime.Split("--")[1])));
                    _streamWriter.FlushAsync();
                    _memoryStream.Seek(0, SeekOrigin.Begin);


                    var formattableString = $"{monthname}.csv";
                    using (var fileStream = File.Create(formattableString))
                    {
                        _memoryStream.CopyTo(fileStream);
                    }

                    List<dynamic> issues;

                    using (var reader1 = new StreamReader($"{monthname}.csv"))
                    using (var csv1 = new CsvReader(reader1, config))
                    {
                        issues = csv1.GetRecords<dynamic>().ToList();
                    }

                    using (var wb = new XLWorkbook())
                    {
                        DataTable table = ToDataTable(issues);
                        var sheet = wb.AddWorksheet(table, "Sheet1");
                        foreach (var ws in wb.Worksheets)
                        {
                            ws.Columns().AdjustToContents();
                        }


                        var formattable = $"{result.Sum(result2 => result2.Hours).ToString()}/{map[DateTime.Parse(result.First().DateTime.Split("--")[1]).Month]}";
                        var gsfds = sheet.LastRow().Cell(1).Value = formattable;
                        //    rng.LastRow().Value = formattable;



                        var output = $"{monthname}.xlsx";
                        wb.SaveAs(output);

                    }
                    File.Delete($"{monthname}.csv");
                }

            }
        }


        private static void GenerateGithubReport()
        {
            var client = new HttpClient();

            var mediaTypeWithQualityHeaderValue =
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.github.cloak-preview+json");
            var typeWithQualityHeaderValue =
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                    "application/vnd.github.groot-preview+json");

            var results = new List<Result>();
            for (int i = 0; i <= 9; i++)
            {
                client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(
                    new System.Net.Http.Headers.ProductInfoHeaderValue("AppName", "1.0"));
                client.DefaultRequestHeaders.Accept.Add(mediaTypeWithQualityHeaderValue);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Token",
                        "");

                var result1 = new Rootobject
                {
                    items = new Item[] { }
                };
                var formattableString = "";
                try
                {
                    formattableString =
                        $"https://api.github.com/search/commits?q=org:eg-brs+author:DanielDziubecki+committer-date:2020-01-01..2020-12-31&sort=committer-date&order=asc&page={i}&per_page=100";
                    // result1 = client.GetFromJsonAsync<Rootobject>(formattableString).GetAwaiter().GetResult();

                    var rawRes = client.GetAsync(formattableString).GetAwaiter().GetResult();
                    if (rawRes.IsSuccessStatusCode)
                    {
                        result1 = rawRes.Content.ReadFromJsonAsync<Rootobject>().GetAwaiter().GetResult();
                    }
                    else
                    {
                        Console.WriteLine(formattableString);
                        Console.WriteLine($"Status: {rawRes.StatusCode}");
                        Console.WriteLine($"Reason: {rawRes.ReasonPhrase}");
                        Console.WriteLine($"Content: {rawRes?.Content.ReadAsStringAsync().Result}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine(formattableString);
                }

                foreach (var result1Item in result1.items)
                {
                    Task.Delay(500).GetAwaiter().GetResult();
                    client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.Add(
                        new System.Net.Http.Headers.ProductInfoHeaderValue("AppName", "1.0"));
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Token",
                            "");
                    client.DefaultRequestHeaders.Accept.Add(typeWithQualityHeaderValue);
                    var res2 = new Class1[] { };
                    string requestUri = "";
                    try
                    {
                        requestUri =
                            $"https://api.github.com/repos/EG-BRS/{result1Item.repository.name}/commits/{result1Item.sha}/pulls";
                        var rawRes = client.GetAsync(requestUri).GetAwaiter().GetResult();
                        if (rawRes.IsSuccessStatusCode)
                        {
                            res2 = rawRes.Content.ReadFromJsonAsync<Class1[]>().GetAwaiter().GetResult();
                        }
                        else
                        {
                            Console.WriteLine(requestUri);
                            Console.WriteLine($"Status: {rawRes.StatusCode}");
                            Console.WriteLine($"Reason: {rawRes.ReasonPhrase}");
                            Console.WriteLine($"Content: {rawRes?.Content.ReadAsStringAsync().Result}");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(requestUri);
                        Console.WriteLine(e.Message);
                    }


                    var result = new Result
                    {
                        Project = result1Item.repository.name,
                        Branch = res2.Length == 0
                            ? "probably master"
                            : string.Join(Environment.NewLine, res2.Select(class1 => class1.head.label)),
                        Comment = result1Item.commit.message,
                        DateTime = result1Item.commit.author.date,
                        Sha = result1Item.sha,
                        PrUrl = res2.Length == 0
                            ? ""
                            : string.Join(Environment.NewLine, res2.Select(class1 => class1.url))
                    };

                    results.Add(result);
                }
            }

            var _memoryStream = new MemoryStream();
            var _streamWriter = new StreamWriter(_memoryStream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
            var _csvWriter = new CsvWriter(_streamWriter, config);

            _csvWriter.WriteRecordsAsync(results.OrderBy(result => result.DateTime));
            _streamWriter.FlushAsync();
            _memoryStream.Seek(0, SeekOrigin.Begin);


            using (var fileStream = File.Create(Directory.GetCurrentDirectory() + "report.csv"))
            {
                _memoryStream.CopyTo(fileStream);
            }
        }
    }




    public class Class1
    {
        public string url { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string html_url { get; set; }
        public string diff_url { get; set; }
        public string patch_url { get; set; }
        public string issue_url { get; set; }
        public int number { get; set; }
        public string state { get; set; }
        public bool locked { get; set; }
        public string title { get; set; }
        public User user { get; set; }
        public string body { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public DateTime closed_at { get; set; }
        public DateTime merged_at { get; set; }
        public string merge_commit_sha { get; set; }
        public object assignee { get; set; }
        public object[] assignees { get; set; }
        public Requested_Reviewers[] requested_reviewers { get; set; }
        public object[] requested_teams { get; set; }
        public object[] labels { get; set; }
        public object milestone { get; set; }
        public bool draft { get; set; }
        public string commits_url { get; set; }
        public string review_comments_url { get; set; }
        public string review_comment_url { get; set; }
        public string comments_url { get; set; }
        public string statuses_url { get; set; }
        public Head head { get; set; }
        public Base _base { get; set; }
        public _Links _links { get; set; }
        public string author_association { get; set; }
        public object auto_merge { get; set; }
        public object active_lock_reason { get; set; }
    }

    public class User
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Head
    {
        public string label { get; set; }
        public string _ref { get; set; }
        public string sha { get; set; }
        public User1 user { get; set; }
        public Repo repo { get; set; }
    }

    public class User1
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Repo
    {
        public int id { get; set; }
        public string node_id { get; set; }
        public string name { get; set; }
        public string full_name { get; set; }
        public bool _private { get; set; }
        public Owner owner { get; set; }
        public string html_url { get; set; }
        public string description { get; set; }
        public bool fork { get; set; }
        public string url { get; set; }
        public string forks_url { get; set; }
        public string keys_url { get; set; }
        public string collaborators_url { get; set; }
        public string teams_url { get; set; }
        public string hooks_url { get; set; }
        public string issue_events_url { get; set; }
        public string events_url { get; set; }
        public string assignees_url { get; set; }
        public string branches_url { get; set; }
        public string tags_url { get; set; }
        public string blobs_url { get; set; }
        public string git_tags_url { get; set; }
        public string git_refs_url { get; set; }
        public string trees_url { get; set; }
        public string statuses_url { get; set; }
        public string languages_url { get; set; }
        public string stargazers_url { get; set; }
        public string contributors_url { get; set; }
        public string subscribers_url { get; set; }
        public string subscription_url { get; set; }
        public string commits_url { get; set; }
        public string git_commits_url { get; set; }
        public string comments_url { get; set; }
        public string issue_comment_url { get; set; }
        public string contents_url { get; set; }
        public string compare_url { get; set; }
        public string merges_url { get; set; }
        public string archive_url { get; set; }
        public string downloads_url { get; set; }
        public string issues_url { get; set; }
        public string pulls_url { get; set; }
        public string milestones_url { get; set; }
        public string notifications_url { get; set; }
        public string labels_url { get; set; }
        public string releases_url { get; set; }
        public string deployments_url { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public DateTime pushed_at { get; set; }
        public string git_url { get; set; }
        public string ssh_url { get; set; }
        public string clone_url { get; set; }
        public string svn_url { get; set; }
        public object homepage { get; set; }
        public int size { get; set; }
        public int stargazers_count { get; set; }
        public int watchers_count { get; set; }
        public string language { get; set; }
        public bool has_issues { get; set; }
        public bool has_projects { get; set; }
        public bool has_downloads { get; set; }
        public bool has_wiki { get; set; }
        public bool has_pages { get; set; }
        public int forks_count { get; set; }
        public object mirror_url { get; set; }
        public bool archived { get; set; }
        public bool disabled { get; set; }
        public int open_issues_count { get; set; }
        public object license { get; set; }
        public int forks { get; set; }
        public int open_issues { get; set; }
        public int watchers { get; set; }
        public string default_branch { get; set; }
    }

    public class Owner
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Base
    {
        public string label { get; set; }
        public string _ref { get; set; }
        public string sha { get; set; }
        public User2 user { get; set; }
        public Repo1 repo { get; set; }
    }

    public class User2
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Repo1
    {
        public int id { get; set; }
        public string node_id { get; set; }
        public string name { get; set; }
        public string full_name { get; set; }
        public bool _private { get; set; }
        public Owner1 owner { get; set; }
        public string html_url { get; set; }
        public string description { get; set; }
        public bool fork { get; set; }
        public string url { get; set; }
        public string forks_url { get; set; }
        public string keys_url { get; set; }
        public string collaborators_url { get; set; }
        public string teams_url { get; set; }
        public string hooks_url { get; set; }
        public string issue_events_url { get; set; }
        public string events_url { get; set; }
        public string assignees_url { get; set; }
        public string branches_url { get; set; }
        public string tags_url { get; set; }
        public string blobs_url { get; set; }
        public string git_tags_url { get; set; }
        public string git_refs_url { get; set; }
        public string trees_url { get; set; }
        public string statuses_url { get; set; }
        public string languages_url { get; set; }
        public string stargazers_url { get; set; }
        public string contributors_url { get; set; }
        public string subscribers_url { get; set; }
        public string subscription_url { get; set; }
        public string commits_url { get; set; }
        public string git_commits_url { get; set; }
        public string comments_url { get; set; }
        public string issue_comment_url { get; set; }
        public string contents_url { get; set; }
        public string compare_url { get; set; }
        public string merges_url { get; set; }
        public string archive_url { get; set; }
        public string downloads_url { get; set; }
        public string issues_url { get; set; }
        public string pulls_url { get; set; }
        public string milestones_url { get; set; }
        public string notifications_url { get; set; }
        public string labels_url { get; set; }
        public string releases_url { get; set; }
        public string deployments_url { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
        public DateTime pushed_at { get; set; }
        public string git_url { get; set; }
        public string ssh_url { get; set; }
        public string clone_url { get; set; }
        public string svn_url { get; set; }
        public object homepage { get; set; }
        public int size { get; set; }
        public int stargazers_count { get; set; }
        public int watchers_count { get; set; }
        public string language { get; set; }
        public bool has_issues { get; set; }
        public bool has_projects { get; set; }
        public bool has_downloads { get; set; }
        public bool has_wiki { get; set; }
        public bool has_pages { get; set; }
        public int forks_count { get; set; }
        public object mirror_url { get; set; }
        public bool archived { get; set; }
        public bool disabled { get; set; }
        public int open_issues_count { get; set; }
        public object license { get; set; }
        public int forks { get; set; }
        public int open_issues { get; set; }
        public int watchers { get; set; }
        public string default_branch { get; set; }
    }

    public class Owner1
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class _Links
    {
        public Self self { get; set; }
        public Html html { get; set; }
        public Issue issue { get; set; }
        public Comments comments { get; set; }
        public Review_Comments review_comments { get; set; }
        public Review_Comment review_comment { get; set; }
        public Commits commits { get; set; }
        public Statuses statuses { get; set; }
    }

    public class Self
    {
        public string href { get; set; }
    }

    public class Html
    {
        public string href { get; set; }
    }

    public class Issue
    {
        public string href { get; set; }
    }

    public class Comments
    {
        public string href { get; set; }
    }

    public class Review_Comments
    {
        public string href { get; set; }
    }

    public class Review_Comment
    {
        public string href { get; set; }
    }

    public class Commits
    {
        public string href { get; set; }
    }

    public class Statuses
    {
        public string href { get; set; }
    }

    public class Requested_Reviewers
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }


    public class Result
    {
        public string Project { get; set; }
        public DateTime DateTime { get; set; }
        public string Branch { get; set; }
        public string Comment { get; set; }
        public string Sha { get; set; }
        public string PrUrl { get; set; }

    }

    public class Result2
    {
        public int Hours { get; set; }
        public string Project { get; set; }
        public string DateTime { get; set; }
        public string Branch { get; set; }
        public string Comment { get; set; }
        public string PrUrl { get; set; }

        public string JiraDescription { get; set; }


    }

    public class Rootobject
    {
        public int total_count { get; set; }
        public bool incomplete_results { get; set; }
        public Item[] items { get; set; }
    }

    public class Item
    {
        public string url { get; set; }
        public string sha { get; set; }
        public string node_id { get; set; }
        public string html_url { get; set; }
        public string comments_url { get; set; }
        public Commit commit { get; set; }
        public Author1 author { get; set; }
        public Committer1 committer { get; set; }
        public Parent[] parents { get; set; }
        public Repository repository { get; set; }
        public float score { get; set; }
    }

    public class Commit
    {
        public string url { get; set; }
        public Author author { get; set; }
        public Committer committer { get; set; }
        public string message { get; set; }
        public Tree tree { get; set; }
        public int comment_count { get; set; }
    }

    public class Author
    {
        public DateTime date { get; set; }
        public string name { get; set; }
        public string email { get; set; }
    }

    public class Committer
    {
        public DateTime date { get; set; }
        public string name { get; set; }
        public string email { get; set; }
    }

    public class Tree
    {
        public string url { get; set; }
        public string sha { get; set; }
    }

    public class Author1
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Committer1
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Repository
    {
        public int id { get; set; }
        public string node_id { get; set; }
        public string name { get; set; }
        public string full_name { get; set; }
        public bool _private { get; set; }
        public Owner2 owner { get; set; }
        public string html_url { get; set; }
        public string description { get; set; }
        public bool fork { get; set; }
        public string url { get; set; }
        public string forks_url { get; set; }
        public string keys_url { get; set; }
        public string collaborators_url { get; set; }
        public string teams_url { get; set; }
        public string hooks_url { get; set; }
        public string issue_events_url { get; set; }
        public string events_url { get; set; }
        public string assignees_url { get; set; }
        public string branches_url { get; set; }
        public string tags_url { get; set; }
        public string blobs_url { get; set; }
        public string git_tags_url { get; set; }
        public string git_refs_url { get; set; }
        public string trees_url { get; set; }
        public string statuses_url { get; set; }
        public string languages_url { get; set; }
        public string stargazers_url { get; set; }
        public string contributors_url { get; set; }
        public string subscribers_url { get; set; }
        public string subscription_url { get; set; }
        public string commits_url { get; set; }
        public string git_commits_url { get; set; }
        public string comments_url { get; set; }
        public string issue_comment_url { get; set; }
        public string contents_url { get; set; }
        public string compare_url { get; set; }
        public string merges_url { get; set; }
        public string archive_url { get; set; }
        public string downloads_url { get; set; }
        public string issues_url { get; set; }
        public string pulls_url { get; set; }
        public string milestones_url { get; set; }
        public string notifications_url { get; set; }
        public string labels_url { get; set; }
        public string releases_url { get; set; }
        public string deployments_url { get; set; }
    }

    public class Owner2
    {
        public string login { get; set; }
        public int id { get; set; }
        public string node_id { get; set; }
        public string avatar_url { get; set; }
        public string gravatar_id { get; set; }
        public string url { get; set; }
        public string html_url { get; set; }
        public string followers_url { get; set; }
        public string following_url { get; set; }
        public string gists_url { get; set; }
        public string starred_url { get; set; }
        public string subscriptions_url { get; set; }
        public string organizations_url { get; set; }
        public string repos_url { get; set; }
        public string events_url { get; set; }
        public string received_events_url { get; set; }
        public string type { get; set; }
        public bool site_admin { get; set; }
    }

    public class Parent
    {
        public string url { get; set; }
        public string html_url { get; set; }
        public string sha { get; set; }
    }

}