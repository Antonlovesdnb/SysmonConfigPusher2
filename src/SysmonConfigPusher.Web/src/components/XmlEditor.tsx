import { useRef, useEffect } from 'react';
import Editor from '@monaco-editor/react';
import type { OnMount, Monaco } from '@monaco-editor/react';
import type { editor, languages, Position } from 'monaco-editor';
import {
  SYSMON_STRUCTURE_ELEMENTS,
  EVENT_ELEMENT_NAMES,
  SYSMON_CONDITIONS,
  ATTRIBUTE_VALUES,
  getFieldsForEvent,
  getEventDescription,
} from '../lib/sysmonSchema';

interface XmlEditorProps {
  value: string;
  onChange?: (value: string) => void;
  readOnly?: boolean;
  height?: string | number;
  fontSize?: number;
  darkMode?: boolean;
}

export function XmlEditor({ value, onChange, readOnly = false, height = '500px', fontSize = 13, darkMode = false }: XmlEditorProps) {
  const editorRef = useRef<editor.IStandaloneCodeEditor | null>(null);
  const monacoRef = useRef<Monaco | null>(null);

  const handleEditorMount: OnMount = (editor, monaco) => {
    editorRef.current = editor;
    monacoRef.current = monaco;

    // Register Sysmon XML completion provider
    monaco.languages.registerCompletionItemProvider('xml', {
      triggerCharacters: ['<', ' ', '"'],
      provideCompletionItems: (model: editor.ITextModel, position: Position) => {
        const textUntilPosition = model.getValueInRange({
          startLineNumber: 1,
          startColumn: 1,
          endLineNumber: position.lineNumber,
          endColumn: position.column,
        });

        const lineContent = model.getLineContent(position.lineNumber);
        const textBeforeCursor = lineContent.substring(0, position.column - 1);

        const suggestions: languages.CompletionItem[] = [];
        const range = {
          startLineNumber: position.lineNumber,
          startColumn: position.column,
          endLineNumber: position.lineNumber,
          endColumn: position.column,
        };

        // Check if we're inside an attribute value (after ="
        const attrValueMatch = textBeforeCursor.match(/(\w+)=["']([^"']*)$/);
        if (attrValueMatch) {
          const attrName = attrValueMatch[1].toLowerCase();

          if (attrName === 'onmatch') {
            ATTRIBUTE_VALUES.onmatch.forEach((val) => {
              suggestions.push({
                label: val,
                kind: monaco.languages.CompletionItemKind.Value,
                insertText: val,
                range,
              });
            });
          } else if (attrName === 'grouprelation') {
            ATTRIBUTE_VALUES.groupRelation.forEach((val) => {
              suggestions.push({
                label: val,
                kind: monaco.languages.CompletionItemKind.Value,
                insertText: val,
                range,
              });
            });
          } else if (attrName === 'condition') {
            SYSMON_CONDITIONS.forEach((val) => {
              suggestions.push({
                label: val,
                kind: monaco.languages.CompletionItemKind.Value,
                insertText: val,
                range,
              });
            });
          }

          return { suggestions };
        }

        // Check if we're inside an opening tag (for attributes)
        const insideTagMatch = textBeforeCursor.match(/<(\w+)(?:\s+[^>]*)?$/);
        if (insideTagMatch && !textBeforeCursor.endsWith('<')) {
          const tagName = insideTagMatch[1];

          // Suggest attributes based on element type
          if (EVENT_ELEMENT_NAMES.includes(tagName)) {
            suggestions.push({
              label: 'onmatch',
              kind: monaco.languages.CompletionItemKind.Property,
              insertText: 'onmatch="${1:exclude}"',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Match type: include or exclude',
              range,
            });
          } else if (tagName === 'RuleGroup') {
            suggestions.push({
              label: 'name',
              kind: monaco.languages.CompletionItemKind.Property,
              insertText: 'name="${1}"',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Rule group name',
              range,
            });
            suggestions.push({
              label: 'groupRelation',
              kind: monaco.languages.CompletionItemKind.Property,
              insertText: 'groupRelation="${1:or}"',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Relation between rules: and or or',
              range,
            });
          } else {
            // Field elements can have condition attribute
            suggestions.push({
              label: 'condition',
              kind: monaco.languages.CompletionItemKind.Property,
              insertText: 'condition="${1:is}"',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Filter condition type',
              range,
            });
          }

          return { suggestions };
        }

        // Check if we just typed '<' - suggest elements
        if (textBeforeCursor.endsWith('<')) {
          // Find the current parent element context
          const parentElement = findParentElement(textUntilPosition);

          if (!parentElement) {
            // Root level - suggest Sysmon
            suggestions.push({
              label: 'Sysmon',
              kind: monaco.languages.CompletionItemKind.Class,
              insertText: 'Sysmon schemaversion="4.90">\n  $0\n</Sysmon>',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Sysmon configuration root element',
              range,
            });
          } else if (parentElement === 'Sysmon') {
            // Inside Sysmon - suggest EventFiltering and options
            suggestions.push({
              label: 'EventFiltering',
              kind: monaco.languages.CompletionItemKind.Module,
              insertText: 'EventFiltering>\n  $0\n</EventFiltering>',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Container for event filtering rules',
              range,
            });
            SYSMON_STRUCTURE_ELEMENTS.filter(e => e !== 'Sysmon' && e !== 'EventFiltering' && e !== 'RuleGroup').forEach((el) => {
              suggestions.push({
                label: el,
                kind: monaco.languages.CompletionItemKind.Property,
                insertText: `${el}>$0</${el}>`,
                insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                range,
              });
            });
          } else if (parentElement === 'EventFiltering') {
            // Inside EventFiltering - suggest RuleGroup
            suggestions.push({
              label: 'RuleGroup',
              kind: monaco.languages.CompletionItemKind.Class,
              insertText: 'RuleGroup name="" groupRelation="or">\n  $0\n</RuleGroup>',
              insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
              documentation: 'Group of filtering rules',
              range,
            });
          } else if (parentElement === 'RuleGroup') {
            // Inside RuleGroup - suggest event filter elements
            EVENT_ELEMENT_NAMES.forEach((eventName) => {
              const desc = getEventDescription(eventName);
              suggestions.push({
                label: eventName,
                kind: monaco.languages.CompletionItemKind.Event,
                insertText: `${eventName} onmatch="exclude">\n  $0\n</${eventName}>`,
                insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                documentation: desc,
                range,
              });
            });
          } else if (EVENT_ELEMENT_NAMES.includes(parentElement)) {
            // Inside an event filter element - suggest field names
            const fields = getFieldsForEvent(parentElement);
            fields.forEach((field) => {
              suggestions.push({
                label: field,
                kind: monaco.languages.CompletionItemKind.Field,
                insertText: `${field} condition="is">$0</${field}>`,
                insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                documentation: `Filter by ${field}`,
                range,
              });
            });
          }

          // Also suggest closing tag
          if (parentElement) {
            suggestions.push({
              label: `/${parentElement}`,
              kind: monaco.languages.CompletionItemKind.Keyword,
              insertText: `/${parentElement}>`,
              documentation: `Close ${parentElement} element`,
              range,
              sortText: 'zzz', // Sort to bottom
            });
          }

          return { suggestions };
        }

        return { suggestions };
      },
    });
  };

  // Find the current parent element from the text
  function findParentElement(text: string): string | null {
    const openTags: string[] = [];
    const tagRegex = /<\/?(\w+)[^>]*>/g;
    let match;

    while ((match = tagRegex.exec(text)) !== null) {
      const fullMatch = match[0];
      const tagName = match[1];

      if (fullMatch.startsWith('</')) {
        // Closing tag
        const lastOpen = openTags.pop();
        if (lastOpen !== tagName) {
          // Mismatched tags, push it back and continue
          if (lastOpen) openTags.push(lastOpen);
        }
      } else if (!fullMatch.endsWith('/>')) {
        // Opening tag (not self-closing)
        openTags.push(tagName);
      }
    }

    return openTags.length > 0 ? openTags[openTags.length - 1] : null;
  }

  // Update theme when darkMode changes
  useEffect(() => {
    if (monacoRef.current) {
      monacoRef.current.editor.setTheme(darkMode ? 'vs-dark' : 'vs');
    }
  }, [darkMode]);

  const handleEditorChange = (value: string | undefined) => {
    if (onChange && value !== undefined) {
      onChange(value);
    }
  };

  return (
    <div className={`border rounded-lg overflow-hidden ${darkMode ? 'border-gray-600' : 'border-gray-300'}`}>
      <Editor
        height={height}
        defaultLanguage="xml"
        theme={darkMode ? 'vs-dark' : 'vs'}
        value={value}
        onChange={handleEditorChange}
        onMount={handleEditorMount}
        options={{
          readOnly,
          minimap: { enabled: true },
          fontSize,
          fontFamily: 'JetBrains Mono, Consolas, Monaco, monospace',
          lineNumbers: 'on',
          folding: true,
          foldingStrategy: 'indentation',
          automaticLayout: true,
          scrollBeyondLastLine: false,
          wordWrap: 'on',
          renderWhitespace: 'selection',
          tabSize: 2,
          quickSuggestions: {
            other: true,
            comments: false,
            strings: true,
          },
          suggestOnTriggerCharacters: true,
          acceptSuggestionOnCommitCharacter: true,
          acceptSuggestionOnEnter: 'on',
          snippetSuggestions: 'top',
        }}
      />
    </div>
  );
}
