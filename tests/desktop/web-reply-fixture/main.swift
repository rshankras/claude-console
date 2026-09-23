// Isolated WKWebView response controls. Never loads a remote page or the real app.
import Cocoa
import WebKit
let fixtureID = "com.vizhi.desktop.testfixture"
guard Bundle.main.bundleIdentifier == fixtureID, CommandLine.arguments.count == 3 else { exit(2) }
let variant = CommandLine.arguments[2]
guard ["normal", "explicit", "code-only", "user-last", "running", "no-ack", "extra-editors",
       "extra-editors-code-only", "extra-editors-duplicate", "no-composer"].contains(variant) else { exit(2) }
let dir = URL(fileURLWithPath: CommandLine.arguments[1])
let board = NSPasteboard(name: NSPasteboard.Name("com.vizhi.fixture.web-copy"))
final class Fixture: NSObject, NSApplicationDelegate, WKScriptMessageHandler, WKNavigationDelegate {
    var window: NSWindow!
    var web: WKWebView!
    func applicationDidFinishLaunching(_ note: Notification) {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        configuration.userContentController.add(self, name: "copy")
        web = WKWebView(frame: NSRect(x: 0, y: 0, width: 800, height: 650), configuration: configuration)
        web.navigationDelegate = self
        window = NSWindow(contentRect: web.frame, styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        window.title = "Vizhi Web Reply Fixture"
        window.contentView = web
        NSApp.setActivationPolicy(.regular)
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        var html = """
        <!doctype html><html><head><style>body{font:18px system-ui;padding:20px}button{margin:4px;padding:8px}h4{font-size:12px} .actions{display:flex;align-items:center;gap:4px}</style></head><body>
        <div><h4>ChatGPT said:</h4><p>Older fixture answer</p><button aria-label="Copy" onclick="copy(this,'old')">Older copy</button><button aria-label="More actions" aria-haspopup="menu">⋯</button></div>
        <div><h4>You said:</h4><p>Disposable test prompt</p><button aria-label="Copy" onclick="copy(this,'user')">User copy</button></div>
        <div><h4>ChatGPT said:</h4><div><p>Full fixture response</p><div><button aria-label="Copy" onclick="copy(this,'code')">Code copy</button><pre>print('fixture')</pre></div></div>
        <div class="actions"><button id="reply-copy" aria-label="Copy" onclick="copy(this,'reply')"><svg width="20" height="20" aria-hidden="true"><rect x="3" y="3" width="12" height="12"/></svg></button><button aria-label="More actions" aria-haspopup="menu">⋯</button></div></div>
        <!--extra-turn-->
        <div role="textbox" contenteditable="true" aria-multiline="true" aria-label="Work with ChatGPT" style="border:1px solid;padding:30px"></div>
        <script>function copy(button,kind){window.webkit.messageHandlers.copy.postMessage(kind);if("\(variant)"!=="no-ack")button.setAttribute('aria-label','Copied')}</script>
        </body></html>
        """
        if variant == "code-only" || variant == "extra-editors-code-only", let start = html.range(of: "<button id=\"reply-copy\""),
           let end = html.range(of: "</button>", range: start.lowerBound..<html.endIndex) {
            html.removeSubrange(start.lowerBound..<end.upperBound)
        }
        if variant == "explicit" { html = html.replacingOccurrences(of: "id=\"reply-copy\" aria-label=\"Copy\"", with: "id=\"reply-copy\" aria-label=\"Copy response\"") }
        if variant == "user-last" { html = html.replacingOccurrences(of: "<!--extra-turn-->", with: "<h4>You said:</h4><p>Newest fixture prompt</p><button aria-label=\"Copy\" onclick=\"copy(this,'user')\">Copy prompt</button>") }
        if variant == "running" { html = html.replacingOccurrences(of: "<!--extra-turn-->", with: "<button aria-label=\"Stop\">Stop</button>") }
        if variant.hasPrefix("extra-editors") {
            // Reproduce the reported structural condition without assuming what the real
            // app's extra fields contain. Neither unrelated text area is a response target.
            html = html.replacingOccurrences(of: "<body>", with: "<body><aside><textarea aria-label=\"Sidebar note\">Fixture sidebar draft</textarea></aside>")
            html = html.replacingOccurrences(of: "<!--extra-turn-->", with: "<textarea aria-label=\"Secondary editor\">Fixture secondary draft</textarea>")
            html = html.replacingOccurrences(of: "<button aria-label=\"More actions\" aria-haspopup=\"menu\">⋯</button>",
                with: "<button aria-label=\"Rate response\">Rate</button><button aria-label=\"Branch in new chat\">Branch</button>")
        }
        if variant == "extra-editors-duplicate" {
            html = html.replacingOccurrences(of: "</body>", with: "<div class=\"actions\"><button aria-label=\"Copy response\" onclick=\"copy(this,'old')\">Ambiguous response copy</button></div></body>")
        }
        if variant == "no-composer" {
            html = html.replacingOccurrences(of: "<div role=\"textbox\" contenteditable=\"true\" aria-multiline=\"true\" aria-label=\"Work with ChatGPT\" style=\"border:1px solid;padding:30px\"></div>", with: "")
        }
        board.clearContents(); board.setString("Unchanged fixture clipboard", forType: .string)
        web.loadHTMLString(html, baseURL: nil)
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        try? Data("ready".utf8).write(to: dir.appendingPathComponent("ready"))
    }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard let kind = message.body as? String, ["old","user","code","reply"].contains(kind) else { return }
        if variant != "no-ack" {
            board.clearContents()
            let text = kind == "reply" ? "Full fixture response" : "Wrong fixture " + kind
            board.setString(text, forType: .string)
            board.setString("<p>\(text)</p>", forType: .html)
        }
        try? Data(kind.utf8).write(to: dir.appendingPathComponent("copied"))
    }
}
let app = NSApplication.shared
let fixture = Fixture()
app.delegate = fixture
app.run()
