using AionDPS.Announcements;

namespace AionDPS.Combat;

/// <summary>Home's news (5 November 2026): the website's list read into items, and where pictures and links may come from.</summary>
public static class SelfCheckNews
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] news on Home (the website's list, pictures and links):");
        const string json = """
            {"ok":true,"items":[
              {"id":"6b0000000000000000000002","source":"game","tag":"Maintenance","title":"November 11 Maintenance","summary":"x","posted":"2026-11-03T06:00:00.000Z",
               "image":"/app/news/art-aion2.jpg","url":"https://aion2.plaync.com/en-us/board/notice/view?articleId=6b0000000000000000000002","start":"2026-11-11T07:00:00.000Z","minutes":240,"done":false},
              {"id":"make-it-yours","source":"atreia","tag":"Atreia App","title":"Make it yours","summary":"A setup.","posted":"2026-11-05T21:00:00Z","image":"/app/news/make-it-yours.jpg",
               "page":"setup","pageLabel":"Run the setup","url":null,"blocks":[{"k":"h","t":"Set it up"},{"k":"li","t":"Drag"},{"k":"img","t":"/app/news/x.jpg"},{"k":"img","t":"https://evil.example/a.png"},{"k":"row","c":["a","b"]},{"k":"script","t":"x"}]},
              {"id":"6b0000000000000000000001","source":"game","tag":"Patch notes","title":"November 5 Update Notes","summary":"Hard mode.","posted":"2026-11-05T06:00:00.000Z",
               "image":"https://evil.example/x.jpg","url":"https://evil.example/","points":["Hard mode added","Bosses softer"]},
              {"id":"NOT-HEX","source":"game","title":"x","posted":"2026-11-05T06:00:00Z"},
              {"id":"no-date","source":"atreia","title":"x"}
            ]}
            """;
        List<NewsItem> items = NewsClient.Parse(json);
        bool read = items.Count == 3 && items[0].Id == "make-it-yours" && items[1].Id == "6b0000000000000000000001" && items[2].Id == "6b0000000000000000000002";
        Console.WriteLine($"  -> three items, newest first, the bad ones left out: {read} ({items.Count})");
        NewsItem ours = items[0];
        bool post = ours.Page == "setup" && ours.PageLabel == "Run the setup" && ours.Image == Brand.Site + "/app/news/make-it-yours.jpg" && ours.Url is null
                    && ours.Blocks is { Count: 4 } b && b[0].Kind == "h" && b[1].Kind == "li" && b[2].Text == Brand.Site + "/app/news/x.jpg" && b[3].Cells.Count == 2;
        Console.WriteLine($"  -> our post: its page, picture and article (a picture from elsewhere and an unknown block left out): {post}");
        NewsItem patch = items[1], maintenance = items[2];
        bool official = patch.Official && patch.Image is null && patch.Url is null && patch.Points.Count == 2 && patch.Blocks is null
                        && maintenance.Start == new DateTime(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc) && maintenance.Minutes == 240 && !maintenance.Done
                        && maintenance.Url == "https://aion2.plaync.com/en-us/board/notice/view?articleId=6b0000000000000000000002";
        Console.WriteLine($"  -> official items: a picture or link from elsewhere left out, the maintenance's start and length: {official}");
        bool rules = NewsClient.PictureUrl("/app/news/../secret.jpg") is null && NewsClient.PictureUrl("https://upload.plaync.com/a/b.png") == "https://upload.plaync.com/a/b.png"
                     && NewsClient.PictureUrl("http://upload.plaync.com/a.png") is null && NewsClient.PictureUrl("https://plaync.com.evil.example/a.png") is null
                     && NewsClient.LinkUrl("/guides/raids") == Brand.Site + "/guides/raids" && NewsClient.LinkUrl("//evil.example/") is null && NewsClient.LinkUrl("https://evil.example/") is null;
        Console.WriteLine($"  -> pictures only from the website and the game's hosts, links only to the website and the official site: {rules}");
        bool broken = NewsClient.Parse("{not json").Count == 0 && NewsClient.Parse("{\"items\":5}").Count == 0;
        Console.WriteLine($"  -> no list from anything else: {broken}");
        return read && post && official && rules && broken;
    }
}
