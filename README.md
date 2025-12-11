# Audio Uploader

This is a C# Console Application that uploads WAV files from a specified directory to the ComsWise API.

## Prerequisites

- .NET SDK (8.0 or later)
- Internet connection to reach `http://localhost:8000`

## Configuration

The following settings are hardcoded in `Program.cs` and can be modified if needed:
- `API_KEY`: Authentication key for the API.
- `API_URL`: Base URL for the API (default: `http://localhost:8000/v1`).
- `FROM_NUMBER`: The caller ID to use for the call records.

## Usage

### Processing Audio Files
To process and upload audio files, use the `process_audio_files` command followed by the directory path:

```bash
dotnet run -- process_audio_files /path/to/wav/files
```

If you omit the directory, you will be prompted to enter it:

```bash
dotnet run -- process_audio_files
```

### Downloading Transcripts
To download transcripts and summaries for existing calls:

```bash
dotnet run -- download
```

This will fetch calls from the API and save their transcripts (if available) as Markdown files in the `transcripts_download` directory.

## Features

- **Concurrency**: Processes 5 files in parallel.
- **Metadata Extraction**: Parses filenames to extract Date, Time, and To-Number.
- **Duration Calculation**: Reads WAV headers to calculate exact duration.
- **Automatic Upload**: 
    1. Requests upload URL.
    2. Uploads file to storage (R2).
    3. Creates a call record in the database.
