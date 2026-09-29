// Controlled AX contract target, not a replica of ChatGPT's web framework.
// AppKit publishes a deterministic AXWebArea/group/text-area hierarchy. This exercises
// the compiled helper's real IPC, setters and events; live framework compatibility is manual.
import Cocoa

let fixtureID = "com.vizhi.desktop.testfixture"
guard Bundle.main.bundleIdentifier == fixtureID, CommandLine.arguments.count == 2 else { exit(2) }
let directory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let stateURL = directory.appendingPathComponent("state.json")
let commandURL = directory.appendingPathComponent("command.json")
let contextBoard = NSPasteboard(name: NSPasteboard.Name("com.vizhi.fixture." + directory.deletingLastPathComponent().lastPathComponent))

final class SearchLink: NSButton {
    let destination: URL
    init(_ title: String, _ url: String, target: AnyObject, action: Selector) {
        destination = URL(string: url)!
        super.init(frame: .zero)
        self.title = title; self.target = target; self.action = action
        setAccessibilityRole(.link)
    }
    required init?(coder: NSCoder) { fatalError() }
    override func accessibilityURL() -> URL? { destination }
}

final class DraftEditor: NSTextView {
    var setterMode = "normal"
    var placeholder = "Work with ChatGPT"
    var valueReadDelay: TimeInterval = 0
    var rejectSelectedRead = false
    override func accessibilitySelectedText() -> String? { rejectSelectedRead ? nil : super.accessibilitySelectedText() }
    var valueSets = 0
    var selectionSets = 0
    var afterValueSet: (() -> Void)?
    override func accessibilityValue() -> String? {
        if valueReadDelay > 0 { Thread.sleep(forTimeInterval: valueReadDelay) }
        if ["placeholder", "rendered-placeholder"].contains(setterMode) && string.isEmpty { return placeholder + "\n" }
        // Model a rich editor whose AX value includes its empty/trailing paragraph.
        if setterMode == "layout" { return string + "\n" }
        return super.accessibilityValue()
    }
    override func accessibilityNumberOfCharacters() -> Int {
        if ["placeholder", "rendered-placeholder"].contains(setterMode) && string.isEmpty { return placeholder.utf16.count + 1 }
        return super.accessibilityNumberOfCharacters()
    }
    override func accessibilityString(for range: NSRange) -> String? {
        // Chromium exposes generated CSS placeholder text through its range API too,
        // while native editing still sees zero characters and keeps the caret at zero.
        if setterMode == "rendered-placeholder" && string.isEmpty {
            let hint = (placeholder + "\n") as NSString
            guard range.location <= hint.length, range.length <= hint.length - range.location else { return nil }
            return hint.substring(with: range)
        }
        return super.accessibilityString(for: range)
    }
    override func setAccessibilityValue(_ value: Any?) {
        valueSets += 1
        if setterMode == "partial" { string = String((value as? String ?? "").prefix(7)) }
        else if setterMode != "reject" { super.setAccessibilityValue(value) }
        afterValueSet?()
    }
    override func setAccessibilitySelectedText(_ selectedText: String?) {
        selectionSets += 1
        if setterMode == "append-partial" { super.setAccessibilitySelectedText(String((selectedText ?? "").prefix(5))) }
        else if setterMode != "reject" { super.setAccessibilitySelectedText(selectedText) }
        afterValueSet?()
    }
}

final class DraftSendButton: NSButton {
    weak var editor: DraftEditor?
    var attachments: (() -> Bool)?
    override func isAccessibilityEnabled() -> Bool { isEnabled }
    override var isEnabled: Bool {
        get { !(editor?.string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ?? true) || attachments?() == true }
        set { super.isEnabled = newValue }
    }
}

final class SelectedChat: NSButton {
    var ariaCurrent = "page"
    // Match the installed app: aria-current is exposed; AXSelected stays false.
    override func isAccessibilitySelected() -> Bool { false }
    override func accessibilityAttributeNames() -> [NSAccessibility.Attribute] {
        super.accessibilityAttributeNames() + [NSAccessibility.Attribute(rawValue: "AXARIACurrent")]
    }
    override func accessibilityAttributeValue(_ attribute: NSAccessibility.Attribute) -> Any? {
        if attribute.rawValue == "AXARIACurrent" { return ariaCurrent }
        return super.accessibilityAttributeValue(attribute)
    }
    override func accessibilityTitle() -> String? { title }
    override func accessibilityPerformPress() -> Bool { true }
}

final class ReplyCopyButton: NSButton {
    var onCopy: (() -> Void)?
    override func accessibilityPerformPress() -> Bool { onCopy?(); return true }
}

final class WindowModel: NSObject {
    let selectedChat = SelectedChat(title: "Fixture original chat", target: nil, action: nil)
    let chatStatus = NSView(frame: .zero)
    let window: NSWindow
    let editor = DraftEditor(frame: NSRect(x: 0, y: 50, width: 550, height: 150))
    let voiceButton = NSButton(title: "Start voice chat", target: nil, action: nil)
    var presses = 0
    var sent: [String] = []
    var imagePastes = 0
    var textPastes = 0
    var copyDuringPaste: String?
    var attachedFiles: [String] = []
    var voice = false
    var searchField: NSTextField = NSSearchField(frame: NSRect(x: 10, y: 90, width: 450, height: 30))
    var searchLinks: [SearchLink] = []
    var searchDelay = 0.0
    var searchPresses = 0
    var searchPanel: NSView!
    var root: NSView!
    var baseChildren: [Any] = []
    let replyTranscript = NSView(frame: .zero)
    var replyCopyPresses = 0
    var openedChat = ""
    var searchOpen = false
    var hideModeDuringSearch = false
    let mode = NSButton(title: "Mode: ChatGPT", target: nil, action: nil)
    init(index: Int) {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 400),
                          styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        super.init()
        window.title = "Vizhi AX Contract Fixture \(index + 1)"
        window.setFrameOrigin(NSPoint(x: 140 + index * 80, y: 180 + index * 50))
        window.isReleasedWhenClosed = false
        root = NSView(frame: NSRect(x: 0, y: 0, width: 640, height: 400))
        root.setAccessibilityElement(true)
        root.setAccessibilityRole(NSAccessibility.Role(rawValue: "AXWebArea"))
        let title = NSTextField(labelWithString: "Controlled AX fixture · no chat, network or audio")
        title.frame = NSRect(x: 25, y: 355, width: 600, height: 25)
        root.addSubview(title)
        mode.frame = NSRect(x: 25, y: 300, width: 170, height: 35)
        let newChat = NSButton(title: "New chat", target: self, action: #selector(pressNewChat))
        newChat.frame = NSRect(x: 210, y: 300, width: 130, height: 35)
        voiceButton.target = self; voiceButton.action = #selector(toggleVoice)
        voiceButton.frame = NSRect(x: 355, y: 300, width: 190, height: 35)
        for button in [mode, newChat, voiceButton] { root.addSubview(button) }
        let composer = NSView(frame: NSRect(x: 25, y: 45, width: 580, height: 220))
        composer.setAccessibilityElement(true)
        composer.setAccessibilityRole(.group)
        composer.setAccessibilityLabel("Composer")
        editor.isEditable = true; editor.isSelectable = true; editor.isRichText = false
        editor.font = NSFont.systemFont(ofSize: 18)
        editor.setAccessibilityLabel("Message")
        composer.addSubview(editor)
        let send = DraftSendButton(title: "Send", target: self, action: #selector(sendDraft))
        send.editor = editor
        send.attachments = { [weak self] in self?.attachedFiles.isEmpty == false }
        send.frame = NSRect(x: 400, y: 0, width: 150, height: 35)
        composer.addSubview(send)
        composer.setAccessibilityChildren([editor, send])
        root.addSubview(composer)
        let searchButton = NSButton(title: "Search", target: self, action: #selector(openSearch))
        searchButton.frame = NSRect(x: 25, y: 265, width: 130, height: 30)
        root.addSubview(searchButton)
        selectedChat.frame = NSRect(x: 25, y: 335, width: 400, height: 30)
        selectedChat.setAccessibilityRole(.row)
        let marker = NSButton(title: "Pin chat", target: nil, action: nil)
        marker.frame = NSRect(x: 330, y: 0, width: 70, height: 30)
        chatStatus.setAccessibilityElement(true)
        chatStatus.setAccessibilityRole(.image)
        chatStatus.setAccessibilityLabel("")
        selectedChat.addSubview(marker); selectedChat.addSubview(chatStatus)
        selectedChat.setAccessibilityChildren([marker, chatStatus])
        root.addSubview(selectedChat)
        replyTranscript.setAccessibilityElement(true)
        replyTranscript.setAccessibilityRole(.group)
        root.addSubview(replyTranscript)
        baseChildren = [mode, newChat, voiceButton, composer, searchButton, selectedChat, replyTranscript]
        root.setAccessibilityChildren(baseChildren)
        searchPanel = NSView(frame: NSRect(x: 30, y: 120, width: 560, height: 130))
        searchPanel.setAccessibilityElement(true)
        searchPanel.setAccessibilityRole(.group)
        searchPanel.setAccessibilitySubrole(NSAccessibility.Subrole(rawValue: "AXApplicationDialog"))
        searchField.setAccessibilityLabel("Search chats")
        searchPanel.addSubview(searchField)
        let a = SearchLink("Plate duration", "https://chatgpt.com/c/plate", target: self, action: #selector(selectResult(_:)))
        let b = SearchLink("Plate design", "https://chatgpt.com/c/design", target: self, action: #selector(selectResult(_:)))
        a.frame = NSRect(x: 10, y: 50, width: 450, height: 30)
        b.frame = NSRect(x: 10, y: 15, width: 450, height: 30)
        searchPanel.addSubview(a); searchPanel.addSubview(b)
        let external = SearchLink("Untrusted conversation", "https://example.com/c/other", target: self, action: #selector(selectResult(_:)))
        let duplicate1 = SearchLink("Duplicate one", "https://chatgpt.com/c/duplicate", target: self, action: #selector(selectResult(_:)))
        let duplicate2 = SearchLink("Duplicate two", "https://chatgpt.com/c/duplicate", target: self, action: #selector(selectResult(_:)))
        for decoy in [external, duplicate1, duplicate2] { searchPanel.addSubview(decoy) }
        searchLinks = [a, b, external, duplicate1, duplicate2]
        searchPanel.setAccessibilityChildren([searchField, a, b, external, duplicate1, duplicate2])
        window.contentView = root
    }
    @objc func pressNewChat() { presses += 1 }
    func configureReply(_ variant: String) {
        replyTranscript.subviews.forEach { $0.removeFromSuperview() }
        replyCopyPresses = 0
        func group(_ children: [NSView]) -> NSView {
            let view = NSView(frame: .zero)
            view.setAccessibilityElement(true); view.setAccessibilityRole(.group)
            for child in children { view.addSubview(child) }
            view.setAccessibilityChildren(children); return view
        }
        func heading(_ text: String) -> NSView {
            let view = group([NSTextField(labelWithString: text)])
            view.setAccessibilityRole(NSAccessibility.Role(rawValue: "AXHeading"))
            // Chromium may put the speaker text in a child, not the heading's title.
            return view
        }
        func copy(_ text: String, label: String = "Copy", response: Bool = false) -> NSView {
            let button = ReplyCopyButton(title: label, target: nil, action: nil)
            button.onCopy = { [weak self, weak button] in
                guard let self else { return }
                self.replyCopyPresses += 1
                if variant != "no-ack" {
                    contextBoard.clearContents()
                    contextBoard.setString(text, forType: .string)
                    contextBoard.setString("<p>\(text)</p>", forType: .html)
                    button?.title = "Copied"
                }
                if variant == "changed" { self.selectedChat.title = "Different fixture chat" }
            }
            let actionLabel = variant == "overflow" ? "More actions" : variant == "continue" ? "Continue in new chat" : "Fork chat from here"
            let fork = NSButton(title: actionLabel, target: nil, action: nil)
            if variant == "overflow" { fork.setAccessibilityRole(.popUpButton) }
            return group(response ? [button, fork] : [button])
        }
        func message(_ text: String, answer: Bool) -> NSView {
            group([heading(answer ? "ChatGPT said:" : "You said:"),
                NSTextField(labelWithString: text), copy(text, response: answer)])
        }
        var rows: [NSView] = []
        if variant != "empty" {
            rows.append(message("Older answer must never win", answer: true))
            rows.append(message("Customer email and misleading Copy text", answer: false))
        }
        if !["empty", "user-last"].contains(variant) {
            var content: [NSView] = [heading("ChatGPT said:"),
                NSTextField(labelWithString: "Thinking and tool details must not be copied"),
                copy("code fragment only"), NSTextField(labelWithString: "Latest complete response")]
            if variant != "code-only" {
                content.append(copy("Verified customer reply", label: variant == "explicit" ? "Copy response" : "Copy", response: true))
            }
            if variant == "duplicate" { content.append(copy("Ambiguous second response", response: true)) }
            rows.append(group(content))
        }
        if variant == "running" { rows.append(NSButton(title: "Stop", target: nil, action: nil)) }
        if variant == "approval" { rows.append(NSButton(title: "Allow once", target: nil, action: nil)) }
        for row in rows { replyTranscript.addSubview(row) }
        replyTranscript.setAccessibilityChildren(rows)
    }
    @objc func openSearch() {
        searchPresses += 1
        if searchDelay > 0 {
            DispatchQueue.main.asyncAfter(deadline: .now() + searchDelay) { self.showSearch() }
        } else { showSearch() }
    }
    func showSearch() {
        searchOpen = true
        root.addSubview(searchPanel)
        mode.setAccessibilityHidden(hideModeDuringSearch)
        root.setAccessibilityChildren((hideModeDuringSearch ? Array(baseChildren.dropFirst()) : baseChildren) + [searchPanel!])
        window.makeFirstResponder(searchField)
    }
    func configureSearch(_ variant: String) {
        searchOpen = false; searchPanel.removeFromSuperview(); root.setAccessibilityChildren(baseChildren)
        mode.setAccessibilityHidden(false)
        hideModeDuringSearch = variant == "modal" || variant == "delayed-modal"
        searchPanel.subviews.forEach { $0.removeFromSuperview() }
        searchField = NSTextField(frame: NSRect(x: 10, y: 90, width: 450, height: 30))
        searchField.placeholderString = "Search…"
        searchField.setAccessibilityLabel("")
        mode.setAccessibilityRole(variant == "group-mode" ? .group : .popUpButton)
        searchPanel.setAccessibilityRole(.group)
        searchPanel.setAccessibilitySubrole(variant == "unsupported" ? nil : NSAccessibility.Subrole(rawValue: "AXApplicationDialog"))
        searchDelay = variant.hasPrefix("delayed") ? 1.6 : 0
        var container = searchPanel!
        if variant == "nested" {
            searchPanel.setAccessibilityRole(.sheet)
            searchPanel.setAccessibilitySubrole(nil)
            let web = NSView(frame: searchPanel.bounds)
            web.setAccessibilityElement(true); web.setAccessibilityRole(NSAccessibility.Role(rawValue: "AXWebArea"))
            searchPanel.addSubview(web); searchPanel.setAccessibilityChildren([web]); container = web
        }
        container.addSubview(searchField)
        searchLinks.forEach { container.addSubview($0) }
        container.setAccessibilityChildren([searchField] + searchLinks)
    }
    @objc func selectResult(_ sender: SearchLink) {
        openedChat = sender.destination.absoluteString
        searchOpen = false
        searchPanel.removeFromSuperview()
        mode.setAccessibilityHidden(false)
        root.setAccessibilityChildren(baseChildren)
    }
    @objc func sendDraft() { sent.append(editor.string); editor.string = "" }
    @objc func toggleVoice() {
        voice.toggle()
        voiceButton.title = voice ? "Stop voice chat" : "Start voice chat"
    }
    var snapshot: [String: Any] {
        ["text": editor.string, "sent": sent, "presses": presses, "voice": voice, "ready": true,
         "keyWindow": window.isKeyWindow, "windowNumber": window.windowNumber,
         "valueSets": editor.valueSets, "selectionSets": editor.selectionSets,
         "query": searchField.stringValue, "searchOpen": searchOpen, "openedChat": openedChat, "searchPresses": searchPresses,
         "replyCopyPresses": replyCopyPresses, "imagePastes": imagePastes, "textPastes": textPastes, "attachedFiles": attachedFiles]
    }
}

final class Fixture: NSObject, NSApplicationDelegate {
    var models: [WindowModel] = []
    var keys = 0
    var shortcutEvents: [[String: Any]] = []
    var lastCommand = ""
    var timer: Timer?
    var eventMonitor: Any?
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        models = (0..<2).map { WindowModel(index: $0) }
        for model in models { model.window.orderFront(nil) }
        models[0].window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        eventMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .keyUp]) { [weak self] event in
            if event.modifierFlags.contains(.command) && [8, 9].contains(event.keyCode) {
                if event.type == .keyDown, let model = self?.models.first(where: { $0.window.windowNumber == event.windowNumber }) {
                    if event.keyCode == 8 {
                        let range = model.editor.selectedRange()
                        if range.length > 0 { contextBoard.clearContents(); contextBoard.setString((model.editor.string as NSString).substring(with: range), forType: .string) }
                    } else if let urls = contextBoard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
                        model.imagePastes += 1
                        for url in urls {
                            model.attachedFiles.append(url.lastPathComponent)
                            let attachment = NSButton(title: url.lastPathComponent, target: nil, action: nil)
                            attachment.frame = NSRect(x: 30, y: 18, width: 580, height: 25)
                            model.root.addSubview(attachment); model.baseChildren.append(attachment)
                        }
                        model.root.setAccessibilityChildren(model.baseChildren)
                    } else if let text = contextBoard.string(forType: .string) {
                        model.textPastes += 1
                        model.editor.insertText(text, replacementRange: model.editor.selectedRange())
                        if let newerCopy = model.copyDuringPaste {
                            contextBoard.clearContents(); contextBoard.setString(newerCopy, forType: .string)
                        }
                    }
                }
                return nil
            }
            // Only the fixture's fixed V test key; never record text or other apps' input.
            if event.keyCode == 9 {
                self?.shortcutEvents.append(["down": event.type == .keyDown,
                                              "flags": event.modifierFlags.rawValue,
                                              "window": event.windowNumber])
                if event.type == .keyDown && event.modifierFlags.contains([.control, .shift]) {
                    self?.keys += 1
                }
                self?.writeState(); return nil
            }
            return event
        }
        timer = Timer.scheduledTimer(withTimeInterval: 0.15, repeats: true) { [weak self] _ in self?.tick() }
    }
    func tick() {
        if let bytes = try? Data(contentsOf: commandURL),
           let command = (try? JSONSerialization.jsonObject(with: bytes)) as? [String: Any],
           let id = command["id"] as? String, id != lastCommand {
            lastCommand = id
            if let window = command["window"] as? Int, window >= 0 && window < models.count {
                models[window].window.makeKeyAndOrderFront(nil)
                NSApp.activate(ignoringOtherApps: true)
            }
            if command["hide"] as? Bool == true { NSApp.hide(nil) }
            if let variant = command["draftVariant"] as? String {
                let editor = models[0].editor
                editor.string = ""; editor.valueSets = 0; editor.selectionSets = 0
                editor.setterMode = variant; editor.afterValueSet = nil
                models[0].copyDuringPaste = command["copyDuringPaste"] as? String
                editor.placeholder = command["draftPlaceholder"] as? String ?? "Work with ChatGPT"
                editor.valueReadDelay = min(0.1, max(0, command["valueReadDelay"] as? Double ?? 0))
                editor.setAccessibilityLabel(["placeholder", "rendered-placeholder"].contains(variant) ? editor.placeholder : "Message")
                editor.setSelectedRange(NSRange(location: 0, length: 0))
                if variant == "switch" {
                    editor.setterMode = "reject"
                    editor.afterValueSet = { [weak self] in self?.models[1].window.makeKeyAndOrderFront(nil) }
                }
            }
            if let text = command["draftText"] as? String {
                models[0].editor.string = text
                models[0].editor.setSelectedRange(NSRange(location: text.utf16.count, length: 0))
            }
            if command["literalTyping"] as? Bool == true {
                models[0].editor.isAutomaticQuoteSubstitutionEnabled = false
                models[0].editor.isAutomaticDashSubstitutionEnabled = false
                models[0].editor.isAutomaticTextReplacementEnabled = false
                models[0].editor.isAutomaticSpellingCorrectionEnabled = false
            }
            if let title = command["draftChat"] as? String { models[0].selectedChat.title = title }
            if let status = command["chatStatus"] as? String { models[0].chatStatus.setAccessibilityLabel(status) }
            if let current = command["chatCurrent"] as? String { models[0].selectedChat.ariaCurrent = current }
            if let role = command["chatStatusRole"] as? String { models[0].chatStatus.setAccessibilityRole(NSAccessibility.Role(rawValue: role)) }
            if let mode = command["draftMode"] as? String { models[0].mode.title = "Mode: " + mode }
            if let length = command["draftSelectionLength"] as? Int {
                models[0].editor.setSelectedRange(NSRange(location: 0, length: length))
            }
            if command["focusEditor"] as? Bool == true { models[0].window.makeFirstResponder(models[0].editor) }
            if let rejected = command["rejectSelectedRead"] as? Bool { models[0].editor.rejectSelectedRead = rejected }
            if let text = command["clipboard"] as? String { contextBoard.clearContents(); contextBoard.setString(text, forType: .string) }
            if let html = command["clipboardHtml"] as? String { contextBoard.setString(html, forType: .html) }
            if let paths = command["clipboardFiles"] as? [String] {
                contextBoard.clearContents(); contextBoard.writeObjects(paths.map { URL(fileURLWithPath: $0) as NSURL })
            }
            if let path = command["clipboardImage"] as? String, let data = try? Data(contentsOf: URL(fileURLWithPath: path)) {
                contextBoard.clearContents(); contextBoard.setData(data, forType: .png)
            }
            if let variant = command["replyVariant"] as? String { models[0].configureReply(variant) }
            if let query = command["query"] as? String { models[1].searchField.stringValue = query }
            if let mode = command["mode"] as? String { models[1].mode.title = "Mode: " + mode }
            if let variant = command["searchVariant"] as? String { models[1].configureSearch(variant) }
            if let hidden = command["modeHidden"] as? Bool {
                let model = models[1]
                model.mode.setAccessibilityHidden(hidden)
                model.root.setAccessibilityChildren((hidden ? Array(model.baseChildren.dropFirst()) : model.baseChildren)
                    + (model.searchOpen ? [model.searchPanel!] : []))
            }
        }
        writeState()
    }
    func writeState() {
        let state: [String: Any] = ["windows": models.map(\.snapshot), "keys": keys, "active": NSApp.isActive,
                                     "shortcutEvents": shortcutEvents,
                                     "command": lastCommand, "contextClipboard": contextBoard.string(forType: .string) ?? "",
                                     "contextHtml": contextBoard.string(forType: .html) ?? "", "pid": ProcessInfo.processInfo.processIdentifier]
        if let bytes = try? JSONSerialization.data(withJSONObject: state, options: [.sortedKeys]) {
            try? bytes.write(to: stateURL, options: .atomic)
        }
    }
}
let app = NSApplication.shared
let fixture = Fixture()
app.delegate = fixture
app.run()
