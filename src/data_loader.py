import os
import re
from pathlib import Path
from typing import List, Dict, Any

class DocumentLoader:
    def __init__(self, raw_dir: Path):
        self.raw_dir = raw_dir

    def load_documents(self) -> List[Dict[str, Any]]:
        documents = []
        if not self.raw_dir.exists():
            return documents

        for file_path in self.raw_dir.glob("*.*"):
            if file_path.suffix.lower() == ".txt":
                docs = self._load_txt(file_path)
                documents.extend(docs)
            elif file_path.suffix.lower() == ".pdf":
                docs = self._load_pdf(file_path)
                documents.extend(docs)
        return documents

    def _load_txt(self, file_path: Path) -> List[Dict[str, Any]]:
        with open(file_path, "r", encoding="utf-8") as f:
            content = f.read()

        filename = file_path.name
        # Split by markdown headers or sections
        sections = re.split(r'\n(?=##?\s|\nMadde\s|\nSoru\s)', content)
        chunks = []

        for idx, section in enumerate(sections):
            clean_text = section.strip()
            if len(clean_text) < 20:
                continue

            # Extract title if available
            first_line = clean_text.split('\n')[0]
            header = re.sub(r'^[#\s]+', '', first_line).strip()

            chunks.append({
                "id": f"{filename}_{idx}",
                "text": clean_text,
                "metadata": {
                    "source": filename,
                    "header": header[:60],
                    "chunk_index": idx
                }
            })

        return chunks

    def _load_pdf(self, file_path: Path) -> List[Dict[str, Any]]:
        try:
            from pypdf import PdfReader
            reader = PdfReader(file_path)
            chunks = []
            filename = file_path.name

            for page_num, page in enumerate(reader.pages):
                text = page.extract_text()
                if not text or len(text.strip()) < 30:
                    continue

                paragraphs = text.split('\n\n')
                for idx, p in enumerate(paragraphs):
                    clean_p = p.strip()
                    if len(clean_p) < 30:
                        continue
                    chunks.append({
                        "id": f"{filename}_p{page_num+1}_{idx}",
                        "text": clean_p,
                        "metadata": {
                            "source": filename,
                            "page": page_num + 1,
                            "header": f"Sayfa {page_num + 1}",
                            "chunk_index": idx
                        }
                    })
            return chunks
        except Exception as e:
            print(f"PDF okuma hatası ({file_path}): {e}")
            return []
