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

1. Open a terminal in this directory.
2. Run the application using `dotnet run` followed by the path to the directory containing your WAV files.

```bash
dotnet run -- "/path/to/your/wav_files_directory"
```

### Example

```bash
dotnet run -- "/Users/gvinay/workplace/matrixbps/RECODINGS/SMARTU"
```

## Features

- **Concurrency**: Processes 5 files in parallel.
- **Metadata Extraction**: Parses filenames to extract Date, Time, and To-Number.
- **Duration Calculation**: Reads WAV headers to calculate exact duration.
- **Automatic Upload**: 
    1. Requests upload URL.
    2. Uploads file to storage (R2).
    3. Creates a call record in the database.
