from pathlib import Path
from typing import List, Dict, Any
from config.settings import CHUNK_SIZE, CHUNK_OVERLAP

class DocumentLoader:
    def __init__(self, data_raw_dir: Path):
        self.data_raw_dir = Path(data_raw_dir)

    def load_documents(self) -> List[Dict[str, Any]]:
        """data/raw dizinindeki tüm metin dosyalarını yükler ve parent-child chunking ile böler."""
        chunks = []
        if not self.data_raw_dir.exists():
            return chunks

        for file_path in self.data_raw_dir.glob("*.txt"):
            try:
                with open(file_path, "r", encoding="utf-8") as f:
                    content = f.read()

                sections = content.split("---")
                for section_idx, section in enumerate(sections):
                    section = section.strip()
                    if not section:
                        continue

                    lines = section.split("\n")
                    header = lines[0].strip() if lines else "Genel Bilgi"
                    body = "\n".join(lines[1:]) if len(lines) > 1 else section

                    body_words = body.split()
                    step = CHUNK_SIZE - CHUNK_OVERLAP
                    if step <= 0:
                        step = CHUNK_SIZE

                    for i in range(0, max(1, len(body_words)), step):
                        chunk_words = body_words[i : i + CHUNK_SIZE]
                        chunk_text = " ".join(chunk_words)
                        if not chunk_text.strip():
                            continue

                        chunks.append({
                            "id": f"{file_path.stem}_sec{section_idx}_chunk{i}",
                            "text": chunk_text,
                            "metadata": {
                                "source": file_path.name,
                                "header": header,
                                "parent_text": section[:1000]
                            }
                        })
            except Exception as e:
                print(f"Hata ({file_path.name}): {e}")

        return chunks
