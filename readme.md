# Danbooru Image Crawler

This tool is a Windows Forms application designed to download images and their corresponding tags from the website `danbooru.donmai.us`.

## Features

1.  **Batch Download by Tags:**
    *   Users can specify one or more tags to search for.
    *   The application will then crawl through all the posts matching the tags, page by page.
    *   For each post, it downloads the high-resolution image (`large_file_url`).
    *   It saves the image's tags into a separate `.txt` file with the same name as the image file. The tags are saved as a comma-separated list.

2.  **Download by Post ID:**
    *   Users can download a single image and its tags by providing a specific Post ID.

3.  **Cookie-based Authentication:**
    *   The application requires user-provided cookies to be entered into the interface. These cookies are used for all download requests, which may be necessary to access certain content or to comply with the site's API usage policies.
    *   The cookies are automatically saved to a `cookies.txt` file when the application is closed and reloaded upon startup.

## How to Use

1.  **Cookies:** Paste your `danbooru.donmai.us` cookies into the "Cookies" textbox.
2.  **Batch Download:**
    *   Enter the tags you want to download in the "Tag" textbox.
    *   Click the "Start" button.
    *   The application will begin downloading images and tag files into an `output` folder within the application's directory.
    *   Click "Stop" to halt the process.
3.  **Single Download:**
    *   Enter the Post ID into the textbox next to the "Download by ID" button.
    *   Click the "Download by ID" button.

## Output

*   **Images:** Saved in the `output` folder.
*   **Tags:** Saved as `.txt` files in the `output` folder, with each file corresponding to an image.
