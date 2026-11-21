"""
Generate individual MP3 audio files from questions.json using the ElevenLabs API.

Usage:
  1. pip install -r requirements.txt
  2. Copy .env.example to .env and paste your ELEVENLABS_API_KEY
  3. Preview character/credit cost first (no API calls):
       python generate_audio.py --dry-run
  4. Generate audio files:
       python generate_audio.py
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

import requests


DEFAULT_VOICE_ID = "JBFqnCBsd6RMkjVDRZzb"  # ElevenLabs premade voice "George"
DEFAULT_MODEL_ID = "eleven_turbo_v2_5"     # 0.5 credits/char via API; fits 100 questions under free 10k tier
DEFAULT_FORMAT = "mp3_44100_128"
ELEVENLABS_TTS_URL = "https://api.elevenlabs.io/v1/text-to-speech/{voice_id}"


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(
        description="Batch-generate individual question MP3 files with ElevenLabs."
    )
    parser.add_argument(
        "--input",
        type=Path,
        default=script_dir / "questions.json",
        help="Path to questions JSON file (default: audio_gen/questions.json).",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=script_dir / "generated_audio",
        help="Directory to write <id>.mp3 files (default: audio_gen/generated_audio).",
    )
    parser.add_argument(
        "--voice-id",
        default=DEFAULT_VOICE_ID,
        help=f"ElevenLabs voice ID (default: {DEFAULT_VOICE_ID}).",
    )
    parser.add_argument(
        "--model-id",
        default=DEFAULT_MODEL_ID,
        help=f"ElevenLabs model ID (default: {DEFAULT_MODEL_ID}). Use eleven_v4 for max expressiveness.",
    )
    parser.add_argument(
        "--output-format",
        default=DEFAULT_FORMAT,
        help=f"Audio output format (default: {DEFAULT_FORMAT}).",
    )
    parser.add_argument(
        "--type",
        dest="question_type",
        default=None,
        help="Optional filter, e.g. 'Behavioral' or 'Technical'.",
    )
    parser.add_argument(
        "--limit",
        type=int,
        default=None,
        help="Optional max number of new clips to generate in this run.",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Regenerate files even if <id>.mp3 already exists.",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.2,
        help="Pause in seconds between API requests (default: 0.2s).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Count questions and characters without calling the ElevenLabs API.",
    )
    return parser.parse_args()


def load_questions(path: Path, question_type: str | None) -> list[dict]:
    if not path.exists():
        raise FileNotFoundError(f"Questions file not found: {path}")

    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)

    questions = data.get("questions", [])
    if question_type:
        target = question_type.strip().lower()
        questions = [q for q in questions if q.get("type", "").strip().lower() == target]
    return questions


def main() -> int:
    script_dir = Path(__file__).resolve().parent
    try:
        from dotenv import load_dotenv
        load_dotenv(script_dir / ".env")
        load_dotenv()
    except ImportError:
        pass

    args = parse_args()

    questions = load_questions(args.input, args.question_type)
    if not questions:
        print("No matching questions found.")
        return 1

    pending: list[tuple[dict, Path]] = []
    for q in questions:
        qid = q["id"]
        out_path = args.output_dir / f"{qid}.mp3"
        if args.overwrite or not out_path.exists():
            pending.append((q, out_path))

    if args.limit is not None:
        pending = pending[: args.limit]

    total_chars = sum(len(q["text"]) for q, _ in pending)
    credit_multiplier = 0.5 if ("flash" in args.model_id or "turbo" in args.model_id) else 1.0
    est_credits = int(total_chars * credit_multiplier)

    print(f"Loaded {len(questions)} questions from {args.input.name}")
    print(f"Pending files to generate: {len(pending)} (skipping {len(questions) - len(pending)} existing)")
    print(f"Total characters: {total_chars:,} | Model: {args.model_id} | Estimated credits: ~{est_credits:,}")

    if args.dry_run:
        print("Dry run complete — no API calls made.")
        return 0

    if not pending:
        print("Nothing to generate; all files already exist.")
        return 0

    api_key = os.getenv("ELEVENLABS_API_KEY")
    if not api_key or api_key == "your_api_key_here":
        print(
            "Error: ELEVENLABS_API_KEY is not set. Edit audio_gen/.env and paste your real API key.",
            file=sys.stderr,
        )
        return 1

    args.output_dir.mkdir(parents=True, exist_ok=True)
    url = ELEVENLABS_TTS_URL.format(voice_id=args.voice_id)
    headers = {
        "xi-api-key": api_key,
        "Content-Type": "application/json",
    }

    generated = 0
    for idx, (q, out_path) in enumerate(pending, start=1):
        qid = q["id"]
        text = q["text"]
        print(f"[{idx}/{len(pending)}] Generating {qid}.mp3 ({len(text)} chars)...")

        resp = requests.post(
            url,
            params={"output_format": args.output_format},
            headers=headers,
            json={
                "text": text,
                "model_id": args.model_id,
            },
            timeout=60,
        )
        if not resp.ok:
            print(
                f"Error generating {qid} (HTTP {resp.status_code}): {resp.text}",
                file=sys.stderr,
            )
            return 1

        out_path.write_bytes(resp.content)
        generated += 1

        if idx < len(pending) and args.delay > 0:
            time.sleep(args.delay)

    print(f"\nDone! Generated {generated} audio files in: {args.output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
