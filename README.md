# YouTube Audio Downloader (MP3)

Prosta aplikacja okienkowa dla systemu Windows (Windows Forms / .NET 10) służąca do pobierania ścieżki dźwiękowej z filmów na YouTube i zapisywania jej bezpośrednio do pliku `.mp3`.

## Główne cechy

- **Brak zewnętrznych zależności** – aplikacja nie wymaga instalowania na komputerze użytkownika `yt-dlp`, `ffmpeg` ani żadnych innych dodatkowych narzędzi wiersza poleceń.
- **Automatyczna konwersja do MP3** – pobiera strumień audio i konwertuje go w locie do formatu MP3 przy użyciu `NAudio.Lame` (z automatycznym fallbackiem do natywnego kodera systemowego Windows Media Foundation).
- **Inteligentne czyszczenie nazwy pliku** – proponowana nazwa pliku powstaje na podstawie tytułu filmu z YouTube po odfiltrowaniu emotikonów oraz znaków niedozwolonych przez system plików Windows (pozostawiane są wyłącznie litery, cyfry oraz bezpieczne znaki dostępne na klawiaturze).
- **Zapamiętywanie folderu docelowego** – wybrany w oknie dialogowym katalog zapisu jest zapamiętywany w `%LocalAppData%\YouTubeAudioDownloader\settings.json` i automatycznie otwierany przy kolejnym pobieraniu.
- **Wygodna obsługa** – przycisk szybkiego wklejania linku ze schowka, pasek postępu pobierania oraz możliwość anulowania operacji w dowolnym momencie.

## Sposób użycia

1. Skopiuj link do filmu z YouTube i kliknij **Wklej** (lub wklej adres ręcznie w polu tekstowym).
2. Kliknij **Pobierz MP3** (lub naciśnij `Enter`).
3. W oknie dialogowym potwierdź lub zmień folder docelowy oraz proponowaną nazwę pliku `.mp3`.
4. Poczekaj na zakończenie pobierania i konwersji.

## Wymagania i kompilacja

- **System operacyjny:** Windows 10 / 11
- **Środowisko:** .NET 10.0 SDK (`net10.0-windows`)

Kompilacja i uruchomienie z wiersza poleceń:

```bash
dotnet build -c Release
dotnet run
```

Pliki wynikowe (plik wykonywalny `.exe` wraz z wymaganymi bibliotekami `.dll`) trafiają do katalogu `bin\Release\net10.0-windows\`.

Aby wygenerować pojedynczy plik instalatora `YouTubeAudioDownloader_Setup.exe` (tworzący skrót na Pulpicie i w Menu Start):

```powershell
.\build-installer.ps1
```

## Wykorzystane biblioteki

- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode) – pobieranie strumieni z YouTube
- [NAudio](https://github.com/naudio/NAudio) & [NAudio.Lame](https://github.com/Corey-M/NAudio.Lame) – dekodowanie strumienia audio i zapis do formatu MP3

## Licencja

Projekt udostępniany na licencji [MIT](LICENSE).
