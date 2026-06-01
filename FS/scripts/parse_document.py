import sys
import os

def main():
    # Force standard output and error to use UTF-8 encoding
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')

    if len(sys.argv) < 2:
        print("Usage: python parse_document.py <file_path>", file=sys.stderr)
        sys.exit(1)
        
    file_path = sys.argv[1]
    if not os.path.exists(file_path):
        print(f"File not found: {file_path}", file=sys.stderr)
        sys.exit(1)

    # 1. Try IBM Docling
    try:
        from docling.document_converter import DocumentConverter
        converter = DocumentConverter()
        result = converter.convert(file_path)
        markdown_text = result.document.export_to_markdown()
        if markdown_text and markdown_text.strip():
            print(markdown_text)
            return
    except Exception as e:
        print(f"Docling parsing failed or not installed: {e}", file=sys.stderr)

    # 2. Try MarkItDown
    try:
        from markitdown import MarkItDown
        md = MarkItDown()
        result = md.convert(file_path)
        markdown_text = result.text_content
        if markdown_text and markdown_text.strip():
            print(markdown_text)
            return
    except Exception as e:
        print(f"MarkItDown parsing failed or not installed: {e}", file=sys.stderr)

    # 3. Last fallback: Filename without extension
    try:
        filename_only = os.path.splitext(os.path.basename(file_path))[0]
        print(filename_only)
    except Exception as e:
        print(f"Filename fallback failed: {e}", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
