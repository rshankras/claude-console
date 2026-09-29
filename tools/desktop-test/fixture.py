"""Compiled AX integration. Target is fixed to our disposable fixture, never an arbitrary app."""
import json
import os
import plistlib
import re
import signal
import subprocess
import sys
import time
from pathlib import Path
from catalog import ROOT

FIXTURE_ID = 'com.vizhi.desktop.testfixture'


def adapter_placeholder_labels():
    """Use the production adapter's exact static vocabulary in native fixture calls."""
    source = (ROOT / 'src/Agents/OpenAiDesktop/OpenAiDesktopAdapter.cs').read_text()
    match = re.search(r'ComposerPlaceholderLabels\s*=>\s*new\[\]\s*\{([^}]+)\}', source, re.S)
    assert match, 'Composer placeholder declaration changed; update fixture extraction explicitly'
    return [json.loads(s) for s in re.findall(r'"(?:[^"\\]|\\.)*"', match[1])]


def review_changes_prompt():
    source = (ROOT / 'src/Core/DesktopActions/DesktopWorkflowCommand.cs').read_text()
    match = re.search(r'Id = "review_changes"[^\n]+?Prompt = ("(?:[^"\\]|\\.)*")', source)
    assert match, 'Review Changes declaration changed; update fixture extraction explicitly'
    return json.loads(match[1])


def check_preset_submission(ax, configure, wait_state, passed, board):
    prompt = review_changes_prompt()
    args = ['--mode-prefix', 'Mode: ', '--expect-mode', 'Codex', '--conv-marker', 'Pin chat',
            '--composer-send-label', 'Send', '--stop', 'Stop', '--approve', 'Allow once',
            '--voice-end', 'Stop voice chat', '--test-pasteboard', board,
            *[arg for hint in adapter_placeholder_labels() for arg in ['--draft-placeholder', hint]]]
    def prepare():
        result = ax('append-target', *args); assert result.get('ok'), result
        return result
    def append(target, retry=False):
        return ax('append', '--text', prompt, '--expect-target', target['target'],
                  '--expect-draft', target['fingerprint'], *args, *(['--accept-existing'] if retry else []))
    def send(target):
        return ax('send', '--send-label', 'Send', '--expect-target', target['target'], '--expect-text', prompt, *args)
    def clean(name):
        configure(name, window=0, draftMode='Codex', draftVariant='rendered-placeholder',
                  draftPlaceholder='Do anything', draftText='', valueReadDelay=0.06,
                  literalTyping=True, focusEditor=True, clipboard='Preserved preset clipboard')

    clean('preset-legacy-whole-text')
    target = prepare()
    result = ax('write', '--text', prompt, '--expect-target', target['target'], *args)
    assert result.get('ok'), result
    wait_state(lambda s: s['windows'][0]['text'] == prompt and s['windows'][0]['sent'] == [])
    assert ax('write', '--text', prompt, '--accept-existing', '--expect-target', target['target'], *args).get('method') == 'existing'
    passed('legacy write now inserts the complete preset under slow AX reads and confirms retry without duplication')

    clean('preset-complete')
    target = prepare()
    result = append(target); assert result.get('ok'), result
    state = wait_state(lambda s: s['windows'][0]['text'] == prompt)
    assert state['windows'][0]['sent'] == []
    assert append(target, retry=True).get('method') == 'existing'
    assert send(target).get('ok')
    wait_state(lambda s: s['windows'][0]['text'] == '' and s['windows'][0]['sent'] == [prompt])
    assert send(target).get('error') == 'no-sendable-draft'
    assert ax('context-clipboard', '--test-pasteboard', board).get('text') == 'Preserved preset clipboard'
    passed('complete preset inserts and sends exactly once under the same slow AX reads; retry and clipboard preserved')

    clean('preset-partial-retry')
    target = prepare()
    configure('preset-partial-state', draftText=prompt[:120], focusEditor=True)
    assert append(target, retry=True).get('error') == 'draft-changed'
    assert send(target).get('error') == 'draft-changed'
    state = wait_state(lambda s: s['command'] == 'preset-partial-state')
    assert state['windows'][0]['text'] == prompt[:120] and state['windows'][0]['sent'] == [prompt]
    passed('retained original fingerprint refuses a partial prefix without appending or submitting it')

    clean('preset-edited')
    target = prepare(); assert append(target).get('ok')
    edited = prompt + ' My additional instruction.'
    configure('preset-user-edit', draftText=edited, focusEditor=True)
    assert send(target).get('error') == 'draft-changed'
    configure('preset-mode-changed', draftMode='ChatGPT')
    assert send(target).get('error') == 'mode-changed'
    state = wait_state(lambda s: s['command'] == 'preset-mode-changed')
    assert state['windows'][0]['text'] == edited and state['windows'][0]['sent'] == [prompt]
    passed('an edited draft or changed mode cannot be auto-submitted')


def check_all_text_entry(ax, configure, wait_state, passed, board):
    text = '  ' + ('Complete instruction: café தமிழ் 😀.\n' * 80) + 'End.  '
    sent = []
    for mode, hint in [('ChatGPT', 'Work with ChatGPT'), ('Codex', 'Do anything')]:
        args = ['--mode-prefix', 'Mode: ', '--expect-mode', mode, '--conv-marker', 'Pin chat',
                '--composer-send-label', 'Send', '--stop', 'Stop', '--approve', 'Allow once',
                '--voice-end', 'Stop voice chat', '--draft-placeholder', hint]
        for variant in ['normal', 'reject', 'rendered-placeholder']:
            state = configure(mode + variant, window=0, draftMode=mode, draftVariant=variant,
                              draftPlaceholder=hint, draftText='', focusEditor=True,
                              clipboard='Preserved text', clipboardHtml='<p>Preserved HTML</p>')
            pastes = state['windows'][0]['textPastes']
            result = ax('write', '--text', text, *args); assert result.get('ok'), result
            state = wait_state(lambda s: s['windows'][0]['text'] == text and s['contextClipboard'] == 'Preserved text' and s['contextHtml'] == '<p>Preserved HTML</p>')
            assert state['windows'][0]['valueSets'] == 0 and state['windows'][0]['selectionSets'] == 1
            assert state['windows'][0]['textPastes'] == pastes + (1 if variant == 'reject' else 0)
            assert state['contextClipboard'] == 'Preserved text' and state['contextHtml'] == '<p>Preserved HTML</p>'
            assert state['windows'][0]['sent'] == sent
            assert ax('write', '--text', text, '--accept-existing', *args).get('method') == 'existing'
            assert ax('write', '--text', text, '--accept-existing', '--send-label', 'Send', *args).get('error') == 'draft-exists'
            passed(mode + '/' + variant + ': complete long Unicode draft, one insertion, clipboard formats, no send, idempotent retry')

        configure(mode + '-send', window=0, draftVariant='reject', draftText='', focusEditor=True)
        prompt = review_changes_prompt()
        assert ax('write', '--text', prompt, '--send-label', 'Send', *args).get('ok')
        sent.append(prompt)
        wait_state(lambda s: s['windows'][0]['text'] == '' and s['windows'][0]['sent'] == sent)
        passed(mode + ': legacy Dictate & Send writes the complete text and submits exactly once')

        state = configure(mode + '-partial', window=0, draftVariant='append-partial', draftText='', focusEditor=True)
        pastes = state['windows'][0]['textPastes']
        assert ax('write', '--text', text, '--send-label', 'Send', *args).get('error') == 'append-unconfirmed'
        assert ax('write', '--text', text, '--accept-existing', *args).get('error') == 'draft-exists'
        state = wait_state(lambda s: s['windows'][0]['text'] == text[:5])
        assert state['windows'][0]['textPastes'] == pastes and state['windows'][0]['sent'] == sent
        passed(mode + ': partial setter never triggers paste, duplicate retry, replacement, or Send')

    configure('reply-source', window=0, draftVariant='normal', draftText='Source', draftSelectionLength=6, focusEditor=True)
    source = ax('context-selection', '--chat-app', 'com.vizhi.fixture.chat-placeholder')['source']
    state = configure('reply-paste', window=0, draftVariant='reject', draftText='', focusEditor=True,
                      clipboard='Original reply clipboard', clipboardHtml='<b>Reply HTML</b>')
    pastes = state['windows'][0]['textPastes']
    assert ax('context-paste', '--source', source, '--text', text).get('ok')
    state = wait_state(lambda s: s['windows'][0]['text'] == text and s['contextClipboard'] == 'Original reply clipboard' and s['contextHtml'] == '<b>Reply HTML</b>')
    assert state['windows'][0]['textPastes'] == pastes + 1 and state['windows'][0]['sent'] == sent
    assert state['contextClipboard'] == 'Original reply clipboard' and state['contextHtml'] == '<b>Reply HTML</b>'
    assert ax('context-paste', '--source', source, '--text', text).get('error') == 'draft-exists'
    passed('legacy reply-field paste uses one whole-text operation, preserves all formats, and refuses a populated field')

    configure('newer-copy', window=0, draftVariant='reject', draftText='', focusEditor=True,
              clipboard='Old clipboard', copyDuringPaste='New external copy')
    assert ax('write', '--text', text).get('ok')
    state = wait_state(lambda s: s['windows'][0]['text'] == text and s['contextClipboard'] == 'New external copy')
    assert state['windows'][0]['sent'] == sent
    passed('a new external clipboard copy made during paste survives restoration')


def check_composer_hints(ax, configure, wait_state, passed, board, work, labels):
    import base64
    def scoped(mode):
        return ['--mode-prefix', 'Mode: ', '--expect-mode', mode, '--conv-marker', 'Pin chat',
                '--composer-send-label', 'Send', '--stop', 'Stop', '--approve', 'Allow once',
                '--voice-end', 'Stop voice chat', '--test-pasteboard', board,
                *[arg for hint in labels for arg in ['--draft-placeholder', hint]]]
    def prepare(mode):
        result = ax('append-target', *scoped(mode)); assert result.get('ok'), result
        return result
    def append(text, target, mode, retry=False):
        return ax('append', '--text', text, '--expect-target', target['target'],
                  '--expect-draft', target['fingerprint'], *scoped(mode), *(['--accept-existing'] if retry else []))
    transcript = 'Explain the current diff. தமிழ் 😀'
    cases = [('Codex', 'Do anything'), ('Codex', 'Describe your task to generate a plan...'),
             ('Codex', 'Describe your goal, define measurable outcomes for best results'),
             ('ChatGPT', 'Ask ChatGPT'), ('ChatGPT', 'Work with ChatGPT')]
    for i, (mode, hint) in enumerate(cases):
        for literal in [False, True]:
            original = hint if literal else ''
            configure(f'hint-{i}-{literal}', window=0, draftMode=mode, draftChat='Composer hint fixture',
                      draftVariant='rendered-placeholder', draftPlaceholder=hint, draftText=original,
                      draftSelectionLength=0, focusEditor=True, clipboard='Keep dictation clipboard')
            target = prepare(mode)
            result = append(transcript, target, mode)
            assert result.get('ok'), (mode, hint, literal, result)
            expected = original + '\n\n' + transcript if original else transcript
            state = wait_state(lambda s: s['windows'][0]['text'] == expected)
            assert state['windows'][0]['sent'] == [] and state['windows'][0]['valueSets'] == 0
            assert append(transcript, target, mode, retry=True).get('method') == 'existing'
            assert ax('context-clipboard', '--test-pasteboard', board).get('text') == 'Keep dictation clipboard'
        passed(mode + ' / ' + hint + ': empty and literal drafts accept transcript without overwrite, duplicate or Send')
    configure('codex-dictation-image', window=0, draftMode='Codex', draftVariant='rendered-placeholder',
              draftPlaceholder='Do anything', draftText='', focusEditor=True)
    target = prepare('Codex')
    path = work / 'codex-dictation.png'
    path.write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII='))
    assert ax('attach-image', '--image', str(path), '--expect-target', target['target'], *scoped('Codex')).get('ok')
    target = prepare('Codex'); assert target['hasContent']
    result = append(transcript, target, 'Codex'); assert result.get('ok'), result
    state = wait_state(lambda s: s['windows'][0]['text'] == transcript)
    assert state['windows'][0]['imagePastes'] == 1 and state['windows'][0]['sent'] == []
    assert append(transcript, target, 'Codex', retry=True).get('method') == 'existing'
    passed('Codex attachment-only draft receives transcript and keeps its image without duplicate insertion')
    configure('codex-dictation-new-words', window=0, draftVariant='rendered-placeholder', draftPlaceholder='Do anything', draftText='', focusEditor=True)
    target = prepare('Codex')
    configure('codex-dictation-user-edit', window=0, draftText='My new Codex notes', draftSelectionLength=0, focusEditor=True)
    assert append(transcript, target, 'Codex').get('error') == 'draft-changed'
    configure('codex-dictation-mode-change', window=0, draftMode='ChatGPT')
    assert append(transcript, target, 'Codex').get('error') == 'mode-changed'
    state = wait_state(lambda s: s['command'] == 'codex-dictation-mode-change')
    assert state['windows'][0]['text'] == 'My new Codex notes' and state['windows'][0]['sent'] == []
    passed('user edits and a mode change after recording begins refuse delayed Codex insertion')
    configure('codex-dictation-send', window=0, draftMode='Codex', draftText=transcript, focusEditor=True)
    assert ax('send', '--send-label', 'Send', *scoped('Codex')).get('ok')
    wait_state(lambda s: s['windows'][0]['text'] == '' and s['windows'][0]['sent'] == [transcript])
    passed('only explicit Send submits the Codex draft')


def check_reply(ax, setup_context, wait_state, passed, args):
    def configure(id, **values):
        # Establish each foreground scenario independently; never refocus during the operation.
        setup_context(id, window=values.pop('window', 0), **values)
        return wait_state(lambda state: state['command'] == id and state['active'])
    copy_args = [*args, '--copy-response', 'Copy response', '--copy-button', 'Copy',
                 '--copy-completed', 'Copied', '--response-action', 'Fork chat from here',
                 '--response-action', 'More actions', '--response-action', 'Continue in new chat',
                 '--assistant-heading', 'ChatGPT said:', '--user-heading', 'You said:',
                 '--stop', 'Stop', '--approve', 'Allow once', '--conv-marker', 'Pin chat',
                 '--state-running', 'Working', '--state-awaiting', 'Awaiting approval',
                 '--voice-end', 'Stop voice chat']
    configure('context-answer', rejectSelectedRead=False, draftText='', draftSelectionLength=0,
                  replyVariant='normal', clipboard='Stale clipboard must not win')
    assert ax('status', *copy_args).get('canCopyAnswer') is True
    answer = ax('copy-reply', *copy_args)
    assert answer.get('ok') and answer['text'] == 'Verified customer reply', answer
    assert ax('context-clipboard', *args).get('text') == answer['text']
    copied_state = wait_state(lambda s: s['windows'][0]['replyCopyPresses'] == 1)
    assert copied_state['contextHtml'] == '<p>Verified customer reply</p>'
    assert copied_state['windows'][0]['text'] == ''
    passed('one tap copies only the latest response action, handles Copy-to-Copied label change, preserves HTML and needs no selection')
    for variant in ['overflow', 'continue']:
        configure('reply-' + variant, replyVariant=variant, clipboard='Stale clipboard')
        assert ax('status', *copy_args).get('canCopyAnswer') is True, variant
        result = ax('copy-reply', *copy_args)
        assert result.get('text') == 'Verified customer reply', (variant, result)
        wait_state(lambda s: s['command'] == 'reply-' + variant and s['windows'][0]['replyCopyPresses'] == 1)
        passed(variant + ' ChatGPT footer copies the latest response without a visible Fork button')
    for variant, error in [('empty', 'reply-unrecognized'), ('user-last', 'no-answer'),
                           ('code-only', 'reply-action-not-found'), ('duplicate', 'reply-copy-multiple'),
                           ('running', 'answer-not-ready'), ('approval', 'answer-not-ready')]:
        configure('reply-' + variant, replyVariant=variant, clipboard='Preserve on refusal')
        assert ax('status', *copy_args).get('canCopyAnswer') is False, variant
        result = ax('copy-reply', *copy_args)
        assert result.get('error') == error, (variant, result)
        unchanged = wait_state(lambda s: s['command'] == 'reply-' + variant)
        assert unchanged['windows'][0]['replyCopyPresses'] == 0
        assert unchanged['contextClipboard'] == 'Preserve on refusal'
    passed('empty/newest-user/incomplete/code-only/ambiguous/running/approval cases never copy an older reply or touch the clipboard')
    configure('reply-sidebar-working', replyVariant='normal', chatStatus='Working', chatStatusRole='AXGroup')
    assert ax('copy-reply', *copy_args).get('error') == 'answer-not-ready'
    configure('reply-sidebar-reset', chatStatus='', replyVariant='explicit')
    assert ax('copy-reply', *copy_args).get('text') == 'Verified customer reply'
    passed('selected sidebar Working blocks copy; exact Copy response tooltip semantics also work')
    configure('reply-no-ack', replyVariant='no-ack', clipboard='No new copy')
    assert ax('copy-reply', *copy_args).get('error') == 'copy-unconfirmed'
    configure('reply-changed', replyVariant='changed')
    assert ax('copy-reply', *copy_args).get('error') == 'answer-changed'
    configure('reply-other-window', window=1)
    assert ax('copy-reply', *copy_args).get('error') == 'reply-unrecognized'
    configure('reply-restore', window=0, replyVariant='empty', draftChat='Fixture original chat')
    passed('copy requires fresh clipboard acknowledgement and remains pinned to its conversation and focused window')
    return answer


def check_append(ax, configure, wait_state, passed, board):
    scoped = ['--mode-prefix', 'Mode: ', '--expect-mode', 'ChatGPT', '--conv-marker', 'Pin chat',
              '--composer-send-label', 'Send', '--draft-placeholder', 'Work with ChatGPT',
              '--stop', 'Stop', '--approve', 'Allow once', '--voice-end', 'Stop voice chat',
              '--test-pasteboard', board]
    def prepare():
        result = ax('append-target', *scoped)
        assert result.get('ok'), result
        assert len(result['fingerprint']) == 64 and 'text' not in result
        return result
    def append(text, target, retry=False):
        return ax('append', '--text', text, '--expect-target', target['target'],
                  '--expect-draft', target['fingerprint'], *scoped, *(['--accept-existing'] if retry else []))
    email = 'Customer email: Friday delivery? தமிழ் 😀'
    instruction = 'Reply politely and confirm Friday.'
    configure('append-empty', window=0, draftVariant='normal', draftText='', focusEditor=True,
              draftMode='ChatGPT', draftChat='Append fixture chat', clipboard='Keep my clipboard')
    first = prepare()
    assert append(email, first).get('ok')
    wait_state(lambda s: s['windows'][0]['text'] == email and not s['windows'][0]['sent'])
    assert append(email, first, retry=True).get('method') == 'existing'
    wait_state(lambda s: s['windows'][0]['text'] == email)
    passed('paste appears immediately; repeating the same operation confirms it without duplication or Send')
    configure('append-selection', window=0, draftSelectionLength=len(email.encode('utf-16-le')) // 2)
    second = prepare()
    assert append(instruction, second).get('ok')
    combined = email + '\n\n' + instruction
    wait_state(lambda s: s['windows'][0]['text'] == combined and s['windows'][0]['valueSets'] == 0)
    passed('dictated instruction appends beneath the email, preserving selected text and never replacing the editor value')
    changed = prepare()
    configure('append-user-edit', window=0, draftText=combined + ' User edit')
    assert append('Do not insert', changed).get('error') == 'draft-changed'
    wait_state(lambda s: s['windows'][0]['text'] == combined + ' User edit')
    changed = prepare()
    configure('append-chat-change', window=0, draftChat='Another fixture chat')
    assert append('Do not retarget', changed).get('error') == 'composer-target-changed'
    passed('user edits and conversation switches invalidate a delayed instruction before insertion')
    for variant in ['reject', 'layout', 'placeholder']:
        initial = '' if variant == 'placeholder' else 'Keep existing draft'
        configure('append-' + variant, window=0, draftVariant=variant, draftText=initial,
                  focusEditor=True, clipboard='Clipboard survives ' + variant)
        target = prepare()
        result = append(email, target)
        assert result.get('ok'), (variant, result)
        if variant == 'reject': assert result['method'] == 'paste'
        expected = (initial + '\n\n' if initial else '') + email
        # The fixture snapshots every 150 ms. Its first matching text snapshot can predate
        # the helper's clipboard restoration, so verify the board and await both states.
        assert ax('context-clipboard', *scoped).get('text') == 'Clipboard survives ' + variant
        state = wait_state(lambda s: s['windows'][0]['text'] == expected
                           and s['contextClipboard'] == 'Clipboard survives ' + variant)
        assert state['windows'][0]['valueSets'] == 0
        assert state['contextClipboard'] == 'Clipboard survives ' + variant
        assert append(email, target, retry=True).get('method') == 'existing'
    passed('ignored AX setter uses native paste; rich-editor paragraphs and placeholder values work; clipboard is restored')
    configure('append-partial', window=0, draftVariant='append-partial', draftText='Existing draft', focusEditor=True)
    target = prepare()
    assert append(email, target).get('error') == 'append-unconfirmed'
    partial = wait_state(lambda s: s['windows'][0]['text'] != 'Existing draft')['windows'][0]['text']
    assert append(email, target, retry=True).get('error') == 'draft-changed'
    wait_state(lambda s: s['windows'][0]['text'] == partial)
    passed('partial insertion is not reported as Pasted and a retry cannot duplicate it')
    configure('append-send', window=0, draftVariant='normal', draftText='', focusEditor=True)
    assert append(email, prepare()).get('ok')
    assert ax('send', '--send-label', 'Send', *scoped).get('ok')
    wait_state(lambda s: s['windows'][0]['sent'] == [email] and s['windows'][0]['text'] == '')
    assert not ax('send', '--send-label', 'Send', *scoped).get('ok')
    passed('only explicit Send submits the completed draft, exactly once')


def check_images(ax, configure, wait_state, passed, board, work):
    import base64
    scoped = ['--mode-prefix', 'Mode: ', '--expect-mode', 'ChatGPT', '--conv-marker', 'Pin chat',
              '--composer-send-label', 'Send', '--draft-placeholder', 'Work with ChatGPT',
              '--stop', 'Stop', '--approve', 'Allow once', '--voice-end', 'Stop voice chat',
              '--test-pasteboard', board]
    images = [work / ('screenshot-' + str(i) + '.png') for i in range(6)]
    for path in images:
        path.write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII='))
    def target():
        result = ax('append-target', *scoped)
        assert result.get('ok'), result
        return result['target']
    def attach(path, pinned):
        return ax('attach-image', '--image', str(path), '--expect-target', pinned, *scoped)
    text = 'Keep my typed question.\nதமிழ் 😀 and a second line.'
    configure('image-existing-draft', window=0, draftMode='ChatGPT', draftChat='Image fixture chat',
              draftVariant='normal', draftText=text, draftSelectionLength=len(text.encode('utf-16-le')) // 2,
              focusEditor=True, clipboard='Preserved screenshot clipboard')
    pinned = target()
    result = attach(images[0], pinned); assert result.get('ok'), result
    assert ax('context-clipboard', '--test-pasteboard', board).get('text') == 'Preserved screenshot clipboard'
    state = wait_state(lambda s: s['windows'][0]['imagePastes'] == 1 and s['contextClipboard'] == 'Preserved screenshot clipboard')
    assert state['windows'][0]['text'] == text and state['windows'][0]['sent'] == []
    assert state['windows'][0]['valueSets'] == 0
    passed('screenshot attaches directly beside an existing selected Unicode draft; text and clipboard survive; nothing sends')
    assert attach(images[0], pinned).get('ok')
    result = attach(images[1], pinned); assert result.get('ok'), result
    state = wait_state(lambda s: s['windows'][0]['imagePastes'] == 2)
    assert state['windows'][0]['text'] == text
    passed('verified duplicate is not pasted again; a second distinct screenshot can be attached')
    configure('image-chat-changed', window=0, draftChat='Another image chat')
    assert attach(images[2], pinned).get('error') == 'composer-target-changed'
    configure('image-mode-changed', window=0, draftMode='Codex')
    assert attach(images[2], pinned).get('error') == 'mode-changed'
    configure('image-window-changed', window=1)
    assert not attach(images[2], pinned).get('ok')
    passed('chat, mode and window changes reject the original screenshot target before paste')
    configure('image-blocked', window=0, draftMode='ChatGPT', draftChat='Image fixture chat', replyVariant='running')
    assert attach(images[2], pinned).get('error') == 'composer-unavailable'
    state = wait_state(lambda s: s['command'] == 'image-blocked')
    assert state['windows'][0]['imagePastes'] == 2
    passed('a running response refuses screenshot paste without a new attachment')
    configure('image-empty', window=0, replyVariant='empty', draftVariant='normal', draftText='', focusEditor=True)
    assert attach(images[3], target()).get('ok')
    configure('image-placeholder', window=0, draftVariant='placeholder', draftText='', focusEditor=True)
    assert attach(images[4], target()).get('ok')
    state = wait_state(lambda s: s['windows'][0]['imagePastes'] == 4)
    assert state['windows'][0]['text'] == '' and state['windows'][0]['sent'] == []
    passed('empty and placeholder composers accept screenshots without adding text or submitting')
    instruction = 'Explain the screenshot supplied with this message in plain language.'
    for name, variant, original in [('empty', 'placeholder', ''), ('literal', 'placeholder', 'Work with ChatGPT'),
                                    ('css-empty', 'rendered-placeholder', ''), ('css-literal', 'rendered-placeholder', 'Work with ChatGPT')]:
        configure('image-explain-' + name, window=0, draftVariant=variant, draftText=original,
                  draftSelectionLength=0, focusEditor=True)
        pinned = ax('append-target', *scoped)
        assert pinned.get('ok') and pinned.get('hasContent'), pinned
        append_args = ['--text', instruction, '--expect-target', pinned['target'], '--expect-draft', pinned['fingerprint'], *scoped]
        result = ax('append', *append_args)
        assert result.get('ok'), (name, result)
        state = wait_state(lambda s: instruction in s['windows'][0]['text'])
        expected = original + '\n\n' + instruction if original else instruction
        assert state['windows'][0]['text'] == expected, state['windows'][0]['text']
        assert state['windows'][0]['imagePastes'] == 4 and state['windows'][0]['sent'] == []
        assert ax('append', *append_args, '--accept-existing').get('method') == 'existing'
        assert ax('context-clipboard', '--test-pasteboard', board).get('text') == 'Preserved screenshot clipboard'
    passed('Screenshot then Explain inserts into an attachment-only placeholder and preserves literal placeholder text; retry never duplicates or sends')
    configure('image-unknown-placeholder', window=0, draftVariant='rendered-placeholder', draftText='', focusEditor=True)
    unknown = [arg for arg in scoped if arg not in ['--draft-placeholder', 'Work with ChatGPT']]
    pinned = ax('append-target', *unknown)
    result = ax('append', '--text', instruction, '--expect-target', pinned['target'],
                '--expect-draft', pinned['fingerprint'], *unknown)
    assert result.get('error') == 'composer-selection-changed', result
    state = wait_state(lambda s: s['command'] == 'image-unknown-placeholder')
    assert state['windows'][0]['text'] == '' and state['windows'][0]['selectionSets'] == 0
    pinned = ax('append-target', *scoped)
    configure('image-changed-notes', window=0, draftText='My new screenshot notes', focusEditor=True)
    result = ax('append', '--text', instruction, '--expect-target', pinned['target'],
                '--expect-draft', pinned['fingerprint'], '--accept-existing', *scoped)
    assert result.get('error') == 'draft-changed', result
    state = wait_state(lambda s: s['command'] == 'image-changed-notes')
    assert state['windows'][0]['text'] == 'My new screenshot notes' and state['windows'][0]['sent'] == []
    assert state['windows'][0]['imagePastes'] == 4
    passed('unconfigured visual hints and user edits still refuse insertion without typing, replacing attachments or sending')


def check_files(ax, configure, wait_state, passed, board, work):
    import base64
    scoped = ['--mode-prefix', 'Mode: ', '--expect-mode', 'ChatGPT', '--conv-marker', 'Pin chat',
              '--composer-send-label', 'Send', '--draft-placeholder', 'Work with ChatGPT',
              '--stop', 'Stop', '--approve', 'Allow once', '--voice-end', 'Stop voice chat',
              '--test-pasteboard', board]
    first, second, third = [work / name for name in ['report.txt', 'data.csv', 'later.txt']]
    first.write_text('A disposable report.'); second.write_text('item,value\nA,3\n'); third.write_text('Another report.')
    def describe(path):
        st = path.stat()
        return dict(Path=str(path), Size=st.st_size, Modified=st.st_mtime_ns // 1_000_000)
    def target():
        value = ax('append-target', *scoped); assert value.get('ok'), value
        return value
    def attach(paths, pinned, descriptions=None):
        return ax('attach-files', '--files', json.dumps(descriptions or [describe(p) for p in paths]),
                  '--expect-target', pinned['target'], *scoped)
    configure('files-start', window=0, draftMode='ChatGPT', draftChat='File workflow', draftText='',
              draftVariant='normal', focusEditor=True, clipboard='Preserved file clipboard')
    assert target()['hasContent'] is False
    configure('copied-files', window=0, clipboardFiles=[str(first), str(second)])
    captured = ax('context-clipboard', '--test-pasteboard', board)
    assert captured.get('files') == [str(first), str(second)] and not captured.get('text'), captured
    passed('Finder file URLs are captured as files rather than filename text')
    configure('files-board', window=0, clipboard='Preserved file clipboard')
    pinned = target(); result = attach([first, second], pinned); assert result.get('ok'), result
    state = wait_state(lambda s: s['windows'][0]['imagePastes'] == 1 and s['contextClipboard'] == 'Preserved file clipboard')
    assert state['windows'][0]['attachedFiles'] == [first.name, second.name]
    assert state['windows'][0]['text'] == '' and state['windows'][0]['sent'] == []
    assert target()['hasContent'] is True
    passed('batch file paste acknowledges every filename, restores clipboard, and exposes attachment-only input as material')
    duplicate = attach([first], pinned); assert duplicate.get('error') == 'attachment-name-exists', duplicate
    assert wait_state(lambda s: s['windows'][0]['imagePastes'] == 1)['windows'][0]['imagePastes'] == 1
    text = 'Keep these notes.\nதமிழ் 😀'
    configure('files-draft', window=0, draftText=text, draftSelectionLength=len(text.encode('utf-16-le')) // 2, focusEditor=True)
    pinned = target(); result = attach([third], pinned); assert result.get('ok'), result
    state = wait_state(lambda s: s['windows'][0]['imagePastes'] == 2 and s['contextClipboard'] == 'Preserved file clipboard')
    assert state['windows'][0]['text'] == text and state['windows'][0]['valueSets'] == 0
    instruction = 'Summarize the documents supplied with this message.'
    pinned = target()
    result = ax('append', '--text', instruction, '--expect-target', pinned['target'], '--expect-draft', pinned['fingerprint'], *scoped)
    assert result.get('ok'), result
    state = wait_state(lambda s: instruction in s['windows'][0]['text'])
    assert state['windows'][0]['text'].startswith(text) and state['windows'][0]['sent'] == []
    assert state['windows'][0]['attachedFiles'] == [first.name, second.name, third.name]
    passed('selected draft survives attachment and source instruction appends without Send or duplicate attachments')
    mutable = work / 'mutable.txt'; mutable.write_text('Original'); descriptor = describe(mutable); mutable.write_text('Changed contents')
    result = attach([mutable], pinned, [descriptor]); assert result.get('error') == 'files-changed', result
    link = work / 'linked.txt'; link.symlink_to(mutable)
    result = attach([link], pinned); assert result.get('error') == 'files-changed', result
    configure('files-mode-change', window=0, draftMode='Codex')
    result = attach([mutable], pinned); assert result.get('error') == 'mode-changed', result
    configure('files-chat-change', window=0, draftMode='ChatGPT', draftChat='Different chat')
    result = attach([mutable], pinned); assert result.get('error') == 'composer-target-changed', result
    passed('changed file metadata, symlinks, mode and conversation changes refuse delayed attachment')
    image = work / 'clipboard-input.png'
    image.write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII='))
    configure('clipboard-image', window=0, clipboardImage=str(image))
    captured = ax('context-clipboard', '--test-pasteboard', board, '--test-capture-dir', str(work / 'captures'))
    assert captured.get('ok') and captured.get('image') and not captured.get('text'), captured
    exported = Path(captured['image']); assert exported.parent == work / 'captures'
    assert exported.read_bytes() == image.read_bytes() and exported.stat().st_mode & 0o777 == 0o600
    configure('image-file-board', window=0, clipboard='Preserved file clipboard')
    result = attach([exported], target()); assert result.get('ok'), result
    assert ax('context-clipboard', '--test-pasteboard', board).get('text') == 'Preserved file clipboard'
    passed('clipboard PNG is exported privately in the fixture directory and attached without filename text')


def run_fixture(directory, only_copy=False, only_append=False, helper_binary=None, only_image=False, only_files=False,
                only_hints=False, placeholder_labels=None, only_prompts=False, only_text=False):
    directory = directory.resolve()  # LaunchServices does not inherit the runner's cwd.
    if sys.platform != 'darwin':
        return dict(status='BLOCKED', reason='Fixture requires a macOS GUI session')
    work = directory / 'fixture'
    work.mkdir(exist_ok=True)
    bundle = work / 'VizhiFixture.app'
    executable = bundle / 'Contents/MacOS/VizhiFixture'
    executable.parent.mkdir(parents=True, exist_ok=True)
    (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier=FIXTURE_ID,
        CFBundleName='Vizhi Test Fixture', CFBundleExecutable='VizhiFixture', CFBundlePackageType='APPL',
        CFBundleVersion='1', NSHighResolutionCapable=True)))
    helper = work / 'VizhiAxBridge'
    logpath = work / 'fixture.log'
    steps = []
    process = None
    app_pid = None
    last_result = None
    with logpath.open('w') as log:
        def command(args, timeout=90):
            return subprocess.run(list(map(str, args)), cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=timeout, check=True)
        def ax(verb, *args):
            nonlocal last_result
            # There is deliberately no --app CLI option exposed by this runner.
            p = subprocess.run([str(helper), verb, '--app', FIXTURE_ID, '--test-pasteboard', 'com.vizhi.fixture.' + directory.name, *args], capture_output=True, text=True, timeout=12)
            value = json.loads(p.stdout.strip())
            last_result = dict(verb=verb, result=value)
            assert not p.stderr.strip(), "AX helper emitted unexpected stderr"
            log.write(json.dumps(dict(verb=verb, result=value)) + '\n'); log.flush()
            return value
        def wait_state(predicate, timeout=8):
            until = time.monotonic() + timeout
            last = None
            while time.monotonic() < until:
                try:
                    last = json.loads((work / 'state.json').read_text())
                    if predicate(last): return last
                except (OSError, ValueError, KeyError, IndexError): pass
                time.sleep(.15)
            raise AssertionError('Fixture state timeout: ' + str(last))
        def passed(name):
            steps.append(dict(name=name, status='PASS'))
        try:
            if helper_binary is None:
                command(['bash', 'tools/desktop/build.sh', '--no-install', '--output', helper])
            else:
                import shutil
                shutil.copy2(helper_binary, helper)
            command(['swiftc', 'tests/desktop/fixture-app/main.swift', '-o', executable], timeout=300)
            command(['codesign', '--force', '--sign', '-', bundle])
            command(['codesign', '--verify', '--strict', bundle])
            passed('compile and sign helper + isolated fixture')
            # The helper checks existing trust without requesting a grant, before finding a target.
            preflight = ax('status')
            if preflight.get('error') == 'not-trusted':
                return dict(status='BLOCKED', reason='Runner lacks Accessibility permission; compiled fixture was not launched. Run from an already authorized local terminal to exercise AX.', steps=steps, log='fixture/fixture.log')
            if preflight.get('error') != 'app-not-running':
                return dict(status='BLOCKED', reason='A fixture instance is already running, or preflight was unexpected. Close that fixture and retry.', steps=steps, log='fixture/fixture.log')
            for name in ['state.json', 'command.json']:
                (work / name).unlink(missing_ok=True)
            # LaunchServices owns foreground GUI launch. Executing a bundle binary directly
            # can leave a fully initialized app in the background under a terminal/agent host.
            process = subprocess.Popen(['/usr/bin/open', '-n', '-W', str(bundle), '--args', str(work)], stdout=log, stderr=subprocess.STDOUT)
            ready = wait_state(lambda s: s.get('pid', 0) > 0 and len(s['windows']) == 2 and all(w.get('ready') for w in s['windows']))
            app_pid = ready['pid']
            ax('focus')
            try:
                wait_state(lambda s: s['active'], timeout=4)
            except AssertionError:
                return dict(status='BLOCKED', reason='The controlled fixture could not become frontmost in this GUI session. Run fixture from an interactive terminal while leaving its window in front.', steps=steps, log='fixture/fixture.log')
            # Production status intentionally reports immediately; wait for the fixture AX tree.
            deadline = time.monotonic() + 8
            while True:
                status = ax('status', '--mode-prefix', 'Mode: ')
                if status.get('ok') and status.get('surface'): break
                if time.monotonic() >= deadline:
                    ax('inspect')  # Only our fixture labels, to diagnose a real integration failure.
                    raise AssertionError('Fixture web accessibility tree did not become available: ' + str(status))
                time.sleep(.25)
            passed('actual AX traversal of controlled AppKit web-area/text-area roles')
            if only_append or only_image or only_files or only_hints or only_prompts or only_text:
                board = 'com.vizhi.fixture.' + directory.name
                def configure(id, **values):
                    (work / 'command.json').write_text(json.dumps(dict(id=id, **values)))
                    return wait_state(lambda state: state['command'] == id and state['active'])
                if only_text:
                    check_all_text_entry(ax, configure, wait_state, passed, board)
                elif only_prompts:
                    check_preset_submission(ax, configure, wait_state, passed, board)
                elif only_hints:
                    check_composer_hints(ax, configure, wait_state, passed, board, work,
                                         adapter_placeholder_labels() if placeholder_labels is None else placeholder_labels)
                elif only_files:
                    check_files(ax, configure, wait_state, passed, board, work)
                elif only_image:
                    check_images(ax, configure, wait_state, passed, board, work)
                else:
                    check_append(ax, configure, wait_state, passed, board)
                return dict(status='PASS', scope='All text entry and recovery' if only_text else 'Complete preset insertion and guarded submission' if only_prompts else 'Codex and ChatGPT dictation hints' if only_hints else 'Files and source workflow' if only_files else 'Immediate screenshot attachment' if only_image else 'Paste into Chat and append dictation', steps=steps, log='fixture/fixture.log')
            if only_copy:
                board = 'com.vizhi.fixture.' + directory.name
                args = ['--test-pasteboard', board, '--chat-app', 'com.vizhi.fixture.chat-placeholder']
                def setup_context(id, **values):
                    (work / 'command.json').write_text(json.dumps(dict(id=id, **values)))
                    return wait_state(lambda state: state['command'] == id)
                check_reply(ax, setup_context, wait_state, passed, args)
                return dict(status='PASS', scope='Copy Reply', steps=steps, log='fixture/fixture.log')
            # Exercise named status images through the complete scanner, CLI and JSON path.
            status_args = ['--conv-marker', 'Pin chat', '--state-awaiting', 'Awaiting approval',
                           '--state-unread', 'Unread', '--state-unread', 'Complete',
                           '--state-running', 'Thinking', '--state-running', 'Working']
            for i, (label, expected) in enumerate([('', 'idle'), ('Thinking', 'running'),
                    ('Complete', 'unread'), ('Awaiting approval', 'awaiting'), ('Unread', 'unread'), ('', 'idle')]):
                command_id = f'conversation-status-{i}'
                (work / 'command.json').write_text(json.dumps(dict(id=command_id, window=0, chatStatus=label)))
                wait_state(lambda s: s['command'] == command_id)
                observed = ax('status', *status_args)
                assert observed.get('conversations'), observed
                assert observed['conversations'][0]['state'] == expected, observed
            passed('conversation status images follow Ready / Thinking / Complete / Allow? independently of global Stop')
            # Reproduce the static installed-UI contract: role=status is an AXGroup named
            # Working, and the current row has AXARIACurrent=page with AXSelected=false.
            for current in ['page', 'false', 'true']:
                command_id = 'conversation-current-' + current
                (work / 'command.json').write_text(json.dumps(dict(id=command_id, window=0,
                    chatStatus='Working', chatStatusRole='AXGroup', chatCurrent=current)))
                wait_state(lambda s: s['command'] == command_id)
                observed = ax('status', *status_args)
                assert observed['conversations'][0]['state'] == 'running', observed
                assert observed['conversations'][0]['selected'] == ('false' if current == 'false' else 'true'), observed
            (work / 'command.json').write_text(json.dumps(dict(id='conversation-current-reset',
                chatStatus='', chatStatusRole='AXImage', chatCurrent='page')))
            wait_state(lambda s: s['command'] == 'conversation-current-reset')
            passed('installed UI semantics: Working status group and aria-current selection with AXSelected false')
            assert ax('press-exact', '--label', 'New chat').get('ok')
            wait_state(lambda s: s['windows'][0]['presses'] == 1 and s['windows'][1]['presses'] == 0)
            passed('exact button dispatch in selected window')
            assert ax('write', '--text', 'Fixture draft alpha').get('ok')
            wait_state(lambda s: s['windows'][0]['text'] == 'Fixture draft alpha' and s['windows'][1]['text'] == '')
            assert ax('write', '--text', 'must not replace').get('error') == 'draft-exists'
            wait_state(lambda s: s['windows'][0]['text'] == 'Fixture draft alpha')
            passed('composer write and existing-draft preservation')
            assert ax('send', '--send-label', 'Send').get('ok')
            wait_state(lambda s: s['windows'][0]['sent'] == ['Fixture draft alpha'])
            assert not ax('send', '--send-label', 'Send').get('ok')
            passed('send existing draft exactly once; empty draft refused')
            (work / 'command.json').write_text(json.dumps(dict(id='second', window=1)))
            wait_state(lambda s: s['command'] == 'second')
            time.sleep(.3)
            assert ax('write', '--text', 'Fixture beta', '--send-label', 'Send').get('ok')
            wait_state(lambda s: s['windows'][1]['sent'] == ['Fixture beta'] and s['windows'][0]['sent'] == ['Fixture draft alpha'])
            passed('second-window targeting and write-then-send')
            assert ax('press', '--label', 'New chat', '--expect-mode', 'Codex', '--mode-prefix', 'Mode: ').get('error') == 'mode-changed'
            passed('mode mismatch refused')
            voice_args = ('--voice-start', 'Start voice chat', '--voice-end', 'Stop voice chat')
            assert ax('voice', '--action', 'start', *voice_args).get('ok')
            wait_state(lambda s: s['windows'][1]['voice'])
            assert ax('voice', '--action', 'start', *voice_args).get('error') == 'voice-state-changed'
            assert ax('voice', '--action', 'end', *voice_args).get('ok')
            wait_state(lambda s: not s['windows'][1]['voice'])
            passed('explicit simulated Voice transitions and stale request refusal (no audio)')
            search_args = ('--mode-prefix', 'Mode: ', '--expect-mode', 'ChatGPT', '--search', 'Search',
                           '--search-field', 'Search chats', '--search-field', 'Search', '--result-host', 'chatgpt.com', '--result-path', '/c/')
            assert ax('write', '--text', 'Preserve this message draft').get('ok')
            opened = ax('search', '--action', 'open', *search_args)
            if not opened.get('ok'):
                ax('inspect')  # The fixture only; never inspect a user's app.
            assert opened.get('ok'), opened
            token = opened['target']
            assert ax('search', '--action', 'read', '--target', token, *search_args).get('ok')
            assert ax('search', '--action', 'focus', '--target', token, '--query', '', *search_args).get('ok')
            written = ax('search', '--action', 'write', '--target', token, '--query', '', '--value', 'plate', *search_args)
            assert written.get('ok') and len(written['results']) == 2, written
            wait_state(lambda s: s['windows'][1]['query'] == 'plate' and s['windows'][1]['text'] == 'Preserve this message draft')
            passed('native search open/focus/write and result links; message draft preserved')
            assert ax('search', '--action', 'write', '--target', token, '--query', '', '--value', 'stale', *search_args).get('error') == 'query-changed'
            assert ax('search', '--action', 'select', '--target', token, '--query', '', '--value', 'https://chatgpt.com/c/design', '--title', 'Plate design', *search_args).get('error') == 'query-changed'
            assert ax('search', '--action', 'select', '--target', token, '--query', 'plate', '--value', 'https://chatgpt.com/c/design', '--title', 'Wrong title', *search_args).get('error') == 'search-result-changed'
            passed('stale query writes and stale/mismatched result selection refused')
            (work / 'command.json').write_text(json.dumps(dict(id='search-mode-change', mode='Codex')))
            wait_state(lambda s: s['command'] == 'search-mode-change')
            assert ax('search', '--action', 'write', '--target', token, '--query', 'plate', '--value', 'wrong mode', *search_args).get('error') == 'mode-changed'
            (work / 'command.json').write_text(json.dumps(dict(id='search-window-change', mode='ChatGPT', window=0)))
            wait_state(lambda s: s['command'] == 'search-window-change')
            assert not ax('search', '--action', 'write', '--target', token, '--query', 'plate', '--value', 'wrong window', *search_args).get('ok')
            (work / 'command.json').write_text(json.dumps(dict(id='search-window-restore', window=1)))
            wait_state(lambda s: s['command'] == 'search-window-restore')
            assert ax('search', '--action', 'select', '--target', token, '--query', 'plate', '--value', 'https://chatgpt.com/c/design', '--title', 'Plate design', *search_args).get('ok')
            wait_state(lambda s: s['windows'][1]['openedChat'] == 'https://chatgpt.com/c/design' and s['windows'][1]['text'] == 'Preserve this message draft')
            assert not ax('search', '--action', 'read', '--target', token, *search_args).get('ok')
            passed('search refuses wrong mode/window, opens exact result, rejects closed search')
            for variant in ['placeholder', 'nested', 'modal', 'group-mode']:
                (work / 'command.json').write_text(json.dumps(dict(id='search-' + variant, searchVariant=variant, window=1)))
                wait_state(lambda s: s['command'] == 'search-' + variant and s['active'])
                opened = ax('search', '--action', 'open', *search_args)
                assert opened.get('ok'), opened
                assert ax('search', '--action', 'write', '--target', opened['target'], '--query', '', '--value', 'topic', *search_args).get('ok')
                wait_state(lambda s: s['windows'][1]['query'] == 'topic' and s['windows'][1]['text'] == 'Preserve this message draft')
                if variant == 'modal':
                    assert ax('status', '--mode-prefix', 'Mode: ').get('mode') == ''
                    assert ax('search', '--action', 'read', '--target', opened['target'], *search_args).get('ok')
                    assert ax('search', '--action', 'focus', '--target', opened['target'], '--query', 'topic', *search_args).get('ok')
                    refused = ax('search', '--action', 'open', *search_args)
                    assert refused.get('error') == 'mode-unavailable' and not refused.get('origin'), refused
                    assert not ax('search', '--action', 'write', '--target', 'wrong-field', '--query', 'topic', '--value', 'wrong', *search_args).get('ok')
                    (work / 'command.json').write_text(json.dumps(dict(id='modal-other-window', window=0)))
                    wait_state(lambda s: s['command'] == 'modal-other-window')
                    assert not ax('search', '--action', 'read', '--target', opened['target'], *search_args).get('ok')
                    (work / 'command.json').write_text(json.dumps(dict(id='modal-mode-changed', window=1, mode='Codex', modeHidden=False)))
                    wait_state(lambda s: s['command'] == 'modal-mode-changed')
                    refused = ax('search', '--action', 'write', '--target', opened['target'], '--query', 'topic', '--value', 'wrong', *search_args)
                    assert refused.get('error') == 'mode-changed' and not refused.get('origin'), refused
                    (work / 'command.json').write_text(json.dumps(dict(id='modal-restore', mode='ChatGPT', modeHidden=True)))
                    wait_state(lambda s: s['command'] == 'modal-restore')
                    assert ax('search', '--action', 'select', '--target', opened['target'], '--query', 'topic', '--value', 'https://chatgpt.com/c/plate', '--title', 'Plate duration', *search_args).get('ok')
                    assert not ax('search', '--action', 'read', '--target', opened['target'], *search_args).get('ok')
            passed('search supports placeholder, nested sheet, hidden modal mode selector and pressable group mode control')
            (work / 'command.json').write_text(json.dumps(dict(id='mode-missing-initially', searchVariant='placeholder', modeHidden=True)))
            before = wait_state(lambda s: s['command'] == 'mode-missing-initially')['windows'][1]['searchPresses']
            refused = ax('search', '--action', 'open', *search_args)
            assert refused.get('error') == 'mode-unavailable' and not refused.get('origin'), refused
            wait_state(lambda s: s['windows'][1]['searchPresses'] == before and s['windows'][1]['query'] == '')
            passed('unverified initial mode cannot open search or issue a recovery token; modal pins reject mode/window changes')
            (work / 'command.json').write_text(json.dumps(dict(id='search-delayed', searchVariant='delayed-modal')))
            before = wait_state(lambda s: s['command'] == 'search-delayed')['windows'][1]['searchPresses']
            pending = ax('search', '--action', 'open', *search_args)
            assert pending.get('error') == 'search-field-missing' and pending.get('origin'), pending
            wait_state(lambda s: s['windows'][1]['searchOpen'])
            recovered = ax('search', '--action', 'probe', '--origin', pending['origin'], *search_args)
            assert recovered.get('ok'), recovered
            assert ax('status', '--mode-prefix', 'Mode: ').get('mode') == ''
            wait_state(lambda s: s['windows'][1]['searchPresses'] == before + 1)
            assert ax('search', '--action', 'probe', '--origin', 'wrong-window', *search_args).get('error') == 'search-target-changed'
            passed('late search field recovered by window-pinned read without another opener press')
            (work / 'command.json').write_text(json.dumps(dict(id='search-hide', hide=True)))
            wait_state(lambda s: s['command'] == 'search-hide' and not s['active'])
            assert ax('search', '--action', 'probe', '--origin', pending['origin'], *search_args).get('error') == 'app-not-frontmost'
            assert ax('search', '--action', 'write', '--target', recovered['target'], '--query', '', '--value', 'background', *search_args).get('error') == 'app-not-frontmost'
            # Restore the selector before the separate explicit-open activation check.
            (work / 'command.json').write_text(json.dumps(dict(id='search-unhide-mode', modeHidden=False)))
            wait_state(lambda s: s['command'] == 'search-unhide-mode')
            assert ax('search', '--action', 'open', *search_args).get('ok')
            wait_state(lambda s: s['active'] and s['windows'][1]['query'] == '')
            passed('explicit Find Chat brings its target app forward; polling and delayed writes refuse background targets')
            (work / 'command.json').write_text(json.dumps(dict(id='search-unsupported', searchVariant='unsupported')))
            before = wait_state(lambda s: s['command'] == 'search-unsupported')['windows'][1]['searchPresses']
            unavailable = ax('search', '--action', 'open', *search_args)
            assert unavailable.get('error') == 'search-container-missing', unavailable
            assert ax('search', '--action', 'open', *search_args).get('error') == 'search-container-missing'
            wait_state(lambda s: s['windows'][1]['searchPresses'] == before + 1 and s['windows'][1]['text'] == 'Preserve this message draft')
            passed('unsupported layout reports the specific reason; Retry does not toggle an already-open search')
            # A posted result alone used to pass while process teardown dropped the events.
            # Observe three separate helper processes, exactly one down/up pair each, in the
            # selected window. Keep the events in the report as real delivery evidence.
            for press in range(1, 4):
                posted = ax('shortcut', '--key-code', '9', '--modifiers', 'control,shift')
                if posted.get('error') == 'app-not-frontmost':
                    return dict(status='BLOCKED', reason='Fixture lost foreground before shortcut delivery. The helper correctly refused; retry while leaving the fixture window in front.', steps=steps, log='fixture/fixture.log')
                assert posted.get('ok')
                try:
                    receipt = wait_state(lambda s: s['keys'] == press and len(s['shortcutEvents']) == press * 2)
                except AssertionError as error:
                    raise AssertionError('Shortcut delivery failed: helper reported posted, but the controlled fixture did not receive one Control+Shift+V down/up pair. ' + str(error)) from error
                down, up = receipt['shortcutEvents'][-2:]
                assert down['down'] and not up['down'], 'Shortcut event order was not key-down, key-up'
                assert all(event['flags'] & 0x1E0000 == 0x60000 for event in [down, up]), 'Shortcut modifier flags changed'
                assert all(event['window'] == receipt['windows'][1]['windowNumber'] for event in [down, up]), 'Shortcut reached the wrong fixture window'
            passed('three configured shortcuts received exactly once, with key-up, modifiers and target window verified')
            steps[-1]['receipts'] = receipt['shortcutEvents']
            # Exercise the real whole-text paste fallback, preserving the fixture clipboard.
            (work / 'command.json').write_text(json.dumps(dict(id='draft-whole-text', window=0, draftVariant='reject')))
            baseline = wait_state(lambda s: s['command'] == 'draft-whole-text')['windows'][0]['sent']
            words = 'A customer asks about a delayed order. Delivery is Friday. Café — வணக்கம் 😀.'
            result = ax('write', '--text', words)
            assert result.get('ok') and result.get('method') == 'paste', result
            wait_state(lambda s: s['windows'][0]['text'] == words and s['windows'][0]['sent'] == baseline
                       and s['windows'][1]['text'] == 'Preserve this message draft')
            assert ax('write', '--text', words, '--accept-existing').get('method') == 'existing'
            assert ax('write', '--text', 'different words', '--accept-existing').get('error') == 'draft-exists'
            assert ax('write', '--text', words, '--accept-existing', '--send-label', 'Send').get('error') == 'draft-exists'
            assert ax('send', '--send-label', 'Send').get('ok')
            wait_state(lambda s: s['windows'][0]['sent'] == baseline + [words] and s['windows'][0]['text'] == '')
            assert not ax('send', '--send-label', 'Send').get('ok')
            passed('refused AX setter falls back to one whole-text paste; retry is idempotent; separate Send submits once')
            (work / 'command.json').write_text(json.dumps(dict(id='draft-layout', window=0, draftVariant='layout')))
            baseline = wait_state(lambda s: s['command'] == 'draft-layout')['windows'][0]['sent']
            # The visible editor is empty, but AX reports its empty paragraph as a newline.
            result = ax('write', '--text', words)
            assert result.get('ok'), result
            wait_state(lambda s: s['windows'][0]['text'] == words and s['windows'][0]['sent'] == baseline)
            assert ax('write', '--text', words, '--accept-existing').get('method') == 'existing'
            assert ax('write', '--text', 'new transcript', '--accept-existing').get('error') == 'draft-exists'
            assert ax('send', '--send-label', 'Send').get('ok')
            wait_state(lambda s: s['windows'][0]['sent'] == baseline + [words] and s['windows'][0]['text'] == '')
            assert not ax('send', '--send-label', 'Send').get('ok')
            passed('empty paragraph permits automatic insertion; trailing paragraph confirms delivery and idempotent retry without clearing real text')
            hint = 'Work with ChatGPT'
            hint_args = ['--draft-placeholder', hint, '--composer-send-label', 'Send']
            for i, transcript in enumerate([words, hint]):
                (work / 'command.json').write_text(json.dumps(dict(id=f'draft-placeholder-{i}', window=0, draftVariant='placeholder')))
                baseline = wait_state(lambda s: s['command'] == f'draft-placeholder-{i}')['windows'][0]['sent']
                # Reproduce the owner's metadata: empty visible text, nonempty AXValue,
                # matching description, 18 reported characters, no AXPlaceholderValue.
                assert ax('write', '--text', transcript).get('error') == 'draft-exists'
                result = ax('write', '--text', transcript, *hint_args)
                assert result.get('ok') and result.get('method') in ['selectedText', 'paste'], result
                wait_state(lambda s: s['windows'][0]['text'] == transcript and s['windows'][0]['sent'] == baseline
                           and s['windows'][0]['valueSets'] == 0 and s['windows'][0]['selectionSets'] == 1)
                assert ax('write', '--text', transcript, '--accept-existing', *hint_args).get('method') == 'existing'
                assert ax('send', '--send-label', 'Send').get('ok')
                wait_state(lambda s: s['windows'][0]['sent'] == baseline + [transcript] and s['windows'][0]['text'] == '')
                assert not ax('send', '--send-label', 'Send').get('ok')
            passed('mirrored empty prompt permits whole-text insertion and exact readback, including a transcript equal to the prompt; no replacement or automatic send')
            (work / 'command.json').write_text(json.dumps(dict(id='draft-real-placeholder', window=0,
                draftVariant='placeholder', draftText=hint, draftSelectionLength=0)))
            wait_state(lambda s: s['command'] == 'draft-real-placeholder')
            assert ax('write', '--text', words, *hint_args).get('error') == 'draft-exists'
            wait_state(lambda s: s['windows'][0]['text'] == hint and s['windows'][0]['valueSets'] == 0
                       and s['windows'][0]['selectionSets'] == 0)
            (work / 'command.json').write_text(json.dumps(dict(id='draft-unconfigured-placeholder', window=0, draftVariant='placeholder')))
            wait_state(lambda s: s['command'] == 'draft-unconfigured-placeholder')
            assert ax('write', '--text', words, '--draft-placeholder', 'Other prompt', '--composer-send-label', 'Send').get('error') == 'draft-exists'
            assert ax('write', '--text', words, '--draft-placeholder', hint).get('error') == 'draft-exists'
            wait_state(lambda s: s['windows'][0]['text'] == '' and s['windows'][0]['valueSets'] == 0
                       and s['windows'][0]['selectionSets'] == 0)
            passed('real draft equal to prompt is preserved even at cursor zero; unknown prompt or missing Send configuration never bypasses the guard')
            (work / 'command.json').write_text(json.dumps(dict(id='workflow-target', window=0, draftVariant='placeholder')))
            wait_state(lambda s: s['command'] == 'workflow-target')
            scoped = ['--mode-prefix', 'Mode: ', '--expect-mode', 'ChatGPT', '--conv-marker', 'Pin chat', *hint_args]
            state = ax('status', '--conv-marker', 'Pin chat')
            if not state.get('conversations'): ax('inspect')
            assert state.get('conversations'), state
            assert state['conversations'][0]['selected'] == 'true', state
            pinned = ax('draft-target', *scoped)
            assert pinned.get('ok') and pinned.get('target'), pinned
            target = pinned['target']
            assert ax('write', '--text', words, '--expect-target', target, *scoped).get('ok')
            assert ax('draft-target', *scoped).get('error') == 'draft-exists'
            assert ax('draft-target', '--allow-existing', *scoped).get('target') == target
            wait_state(lambda s: s['windows'][0]['text'] == words)
            passed('spoken workflow pins empty editor and mode; completed brief inserts without automatic send')
            (work / 'command.json').write_text(json.dumps(dict(id='workflow-other-window', window=1)))
            wait_state(lambda s: s['command'] == 'workflow-other-window')
            assert ax('send', '--send-label', 'Send', '--expect-target', target, *scoped).get('error') == 'composer-target-changed'
            (work / 'command.json').write_text(json.dumps(dict(id='workflow-mode-change', window=0, draftMode='Codex')))
            wait_state(lambda s: s['command'] == 'workflow-mode-change')
            assert ax('write', '--text', 'late brief', '--expect-target', target, *scoped).get('error') == 'mode-changed'
            (work / 'command.json').write_text(json.dumps(dict(id='workflow-target-restore', window=0, draftMode='ChatGPT')))
            wait_state(lambda s: s['command'] == 'workflow-target-restore')
            (work / 'command.json').write_text(json.dumps(dict(id='workflow-chat-change', draftChat='Fixture other chat')))
            wait_state(lambda s: s['command'] == 'workflow-chat-change')
            assert ax('send', '--send-label', 'Send', '--expect-target', target, *scoped).get('error') == 'composer-target-changed'
            (work / 'command.json').write_text(json.dumps(dict(id='workflow-chat-restore', draftChat='Fixture original chat')))
            wait_state(lambda s: s['command'] == 'workflow-chat-restore')
            assert ax('send', '--send-label', 'Send', '--expect-target', target, *scoped).get('ok')
            wait_state(lambda s: s['windows'][0]['text'] == '' and s['windows'][1]['text'] == 'Preserve this message draft')
            passed('workflow send and late write refuse a different window or mode; restored target sends reviewed draft once')
            (work / 'command.json').write_text(json.dumps(dict(id='draft-partial', window=0, draftVariant='append-partial')))
            wait_state(lambda s: s['command'] == 'draft-partial')
            assert ax('write', '--text', words).get('error') == 'append-unconfirmed'
            wait_state(lambda s: s['windows'][0]['text'] == words[:5] and s['windows'][0]['selectionSets'] == 1)
            assert ax('write', '--text', words, '--accept-existing').get('error') == 'draft-exists'
            passed('partial AX write never receives a duplicate transcript or automatic send')
            (work / 'command.json').write_text(json.dumps(dict(id='draft-target-change', window=0, draftVariant='switch')))
            wait_state(lambda s: s['command'] == 'draft-target-change')
            assert ax('write', '--text', words).get('error') == 'append-unconfirmed'
            wait_state(lambda s: s['windows'][0]['text'] == '' and s['windows'][0]['selectionSets'] == 1
                       and s['windows'][1]['text'] == 'Preserve this message draft')
            passed('window change during AX settle prevents every subsequent insertion fallback')
            board = 'com.vizhi.fixture.' + directory.name
            args = ['--test-pasteboard', board, '--chat-app', 'com.vizhi.fixture.chat-placeholder']
            def setup_context(id, **values):
                (work / 'command.json').write_text(json.dumps(dict(id=id, **values)))
                return wait_state(lambda state: state['command'] == id)
            setup_context('context-selection', window=0, draftVariant='normal', draftText='Customer email',
                          draftSelectionLength=8, focusEditor=True, clipboard='Unrelated clipboard')
            selected = ax('context-selection', *args)
            assert selected.get('ok') and selected['text'] == 'Customer', selected
            source = selected['source']
            copied = ax('context-clipboard', *args)
            assert copied.get('text') == 'Unrelated clipboard', copied
            setup_context('context-native-copy', rejectSelectedRead=True, draftSelectionLength=8, clipboard='Keep this clipboard')
            selected = ax('context-selection', *args)
            assert selected.get('text') == 'Customer', selected
            assert ax('context-clipboard', *args).get('text') == 'Keep this clipboard'
            setup_context('context-no-selection', draftSelectionLength=0)
            assert ax('context-selection', *args).get('error') == 'no-selection'
            assert ax('context-clipboard', *args).get('text') == 'Keep this clipboard'
            passed('source selection uses AX or fresh Copy, restores isolated clipboard, and never reuses stale clipboard when selection is empty')
            answer = check_reply(ax, setup_context, wait_state, passed, args)
            setup_context('context-return-other', window=1)
            assert ax('context-return', '--source', source, *args).get('ok')
            setup_context('context-empty-reply', window=0, draftVariant='normal', focusEditor=True)
            pasted = ax('context-paste', '--source', source, '--text', answer['text'], *args)
            assert pasted.get('ok'), pasted
            wait_state(lambda state: state['windows'][0]['text'] == answer['text'])
            assert ax('context-paste', '--source', source, '--text', answer['text'], *args).get('error') == 'draft-exists'
            passed('latest copied reply returns to the source window, fills an empty reply field, and refuses duplicate insertion without send')
            import base64
            path = work / 'context-fixture.png'
            path.write_bytes(base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII='))
            setup_context('context-image', window=0, draftVariant='normal', focusEditor=True, clipboard='Preserve through image paste')
            scoped = ['--mode-prefix', 'Mode: ', '--expect-mode', 'ChatGPT', '--conv-marker', 'Pin chat', *hint_args]
            target = ax('draft-target', *scoped)['target']
            image_result = ax('attach-image', '--image', str(path), '--expect-target', target, *scoped, '--test-pasteboard', board)
            assert image_result.get('ok'), image_result
            assert ax('context-clipboard', *args).get('text') == 'Preserve through image paste'
            assert ax('attach-image', '--image', str(path), '--expect-target', target, *scoped, '--test-pasteboard', board).get('ok')
            wait_state(lambda state: state['windows'][0]['text'] == '')
            passed('image file paste confirms the attachment name, preserves the isolated clipboard, leaves text empty, and recognizes an existing attachment')
            (work / 'command.json').write_text(json.dumps(dict(id='hide', hide=True)))
            wait_state(lambda s: s['command'] == 'hide' and not s['active'])
            assert ax('shortcut', '--key-code', '9', '--modifiers', 'control,shift').get('error') == 'app-not-frontmost'
            # Allow another fixture heartbeat; a wrongly delivered background event must
            # not hide behind a state snapshot from before the refused shortcut.
            time.sleep(.3)
            wait_state(lambda s: s['keys'] == 3 and len(s['shortcutEvents']) == 6)
            passed('shortcut refuses background target without event delivery')
            return dict(status='PASS', steps=steps, log='fixture/fixture.log', limitation='Controlled AppKit AX roles only. Does not establish Chromium/WebKit composer compatibility, real ChatGPT behavior, or voice audio.')
        except (OSError, subprocess.SubprocessError, ValueError, AssertionError) as error:
            return dict(status='FAIL', reason=str(error) or ('AX assertion failed: ' + json.dumps(last_result)), steps=steps, log='fixture/fixture.log')
        finally:
            if app_pid:
                # Terminate only our exact fixture executable, never an app that reused a PID.
                current = subprocess.run(['/bin/ps', '-p', str(app_pid), '-o', 'command='], capture_output=True, text=True)
                if current.stdout.strip().startswith(str(executable) + ' '):
                    try: os.kill(app_pid, signal.SIGTERM)
                    except ProcessLookupError: pass
            if process:
                process.terminate()
                try: process.wait(timeout=5)
                except subprocess.TimeoutExpired: process.kill(); process.wait()
