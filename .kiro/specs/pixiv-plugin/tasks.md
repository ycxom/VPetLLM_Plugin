# Implementation Plan

- [x] 1. Set up project structure and data models




  - [ ] 1.1 Create PixivPlugin project with csproj file
    - Create PixivPlugin folder and PixivPlugin.csproj referencing VPetLLM.Core
    - Target net8.0-windows with WPF support


    - Add Newtonsoft.Json dependency
    - _Requirements: 1.1, 1.2_
  - [ ] 1.2 Implement data models for Pixiv API responses
    - Create Models folder with PixivIllust.cs, PixivUser.cs, PixivImageUrls.cs, PixivTag.cs
    - Create PixivSearchResponse.cs and PixivRankingResponse.cs
    - Add JSON serialization attributes


    - _Requirements: 1.2_
  - [ ]* 1.3 Write property test for JSON round-trip
    - **Property 1: JSON Response Round-Trip Consistency**
    - **Validates: Requirements 1.2**




  - [ ] 1.4 Implement PluginSettings model
    - Create PluginSettings.cs with UseProxy, FollowVPetLLMProxy, ProxyUrl properties
    - _Requirements: 6.4_
  - [x]* 1.5 Write property test for settings round-trip

    - **Property 6: Settings Persistence Round-Trip**
    - **Validates: Requirements 6.4**

- [ ] 2. Implement API service layer
  - [ ] 2.1 Create PixivApiService class
    - Implement SearchAsync(string keyword) method

    - Implement GetRankingAsync(string mode) method
    - Add Authorization header with API key
    - Handle HTTP errors and timeouts
    - _Requirements: 1.1, 2.1_
  - [x] 2.2 Implement keyword validation

    - Reject empty or whitespace-only keywords
    - Return appropriate error message
    - _Requirements: 1.4_




  - [ ]* 2.3 Write property test for whitespace rejection
    - **Property 2: Empty/Whitespace Keyword Rejection**
    - **Validates: Requirements 1.4**

  - [ ] 2.4 Implement random selection for ranking
    - Select random Illust from ranking results
    - _Requirements: 2.2_
  - [x]* 2.5 Write property test for random selection

    - **Property 3: Random Selection Membership**
    - **Validates: Requirements 2.2**

- [ ] 3. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.





- [ ] 4. Implement image loading with anti-hotlinking
  - [ ] 4.1 Create ImageLoader class
    - Implement LoadImageAsync(string url) method
    - Add Referer header "https://www.pixiv.net/" to all requests
    - Support optional proxy configuration

    - _Requirements: 5.1, 5.2, 5.4_
  - [ ] 4.2 Implement image download functionality
    - Implement DownloadImageAsync(string url, string savePath, IProgress<int> progress) method
    - Include Referer header for downloads
    - Report download progress
    - _Requirements: 4.1, 4.2, 4.4_
  - [ ] 4.3 Implement URL resolution logic
    - For single-page (page_count == 1): use meta_single_page.original_image_url



    - For multi-page (page_count > 1): use meta_pages[index].image_urls.original

    - _Requirements: 4.1, 8.1, 8.3_
  - [ ]* 4.4 Write property test for URL resolution
    - **Property 5: Original Image URL Resolution**

    - **Validates: Requirements 4.1, 8.1, 8.3**

- [x] 5. Implement preview window UI



  - [-] 5.1 Create winPixivPreview.xaml

    - Add Image control for thumbnail display

    - Add TextBlocks for title, author, tags
    - Add navigation buttons (Previous, Next)
    - Add download button with progress bar
    - Add page indicator (current/total)

    - _Requirements: 3.1, 3.6, 7.1, 7.2, 7.3, 7.4, 8.4_
  - [ ] 5.2 Implement winPixivPreview.xaml.cs
    - Implement image loading and display


    - Implement navigation logic (next/previous)
    - Handle button enable/disable at boundaries
    - Implement download with progress
    - _Requirements: 3.2, 3.3, 3.4, 3.5, 4.3, 4.5_
  - [ ]* 5.3 Write property test for navigation logic
    - **Property 4: Navigation Index Bounds**
    - **Validates: Requirements 3.2, 3.3**

- [ ] 6. Implement settings window UI
  - [ ] 6.1 Create winPixivSetting.xaml
    - Add CheckBox for enable proxy
    - Add CheckBox for follow VPetLLM proxy
    - Add TextBox for proxy URL
    - Add Save/Cancel buttons
    - _Requirements: 6.1, 6.2, 6.3_
  - [ ] 6.2 Implement winPixivSetting.xaml.cs
    - Load current settings on open
    - Save settings on confirm
    - _Requirements: 6.4_

- [ ] 7. Implement main plugin class
  - [ ] 7.1 Create PixivPlugin.cs implementing IActionPlugin
    - Implement Name, Author, Description, Parameters, Examples properties
    - Implement Initialize, Function, Unload methods
    - _Requirements: 1.1, 2.1_
  - [ ] 7.2 Implement Function method argument parsing
    - Parse action(search/random) parameter
    - Parse keyword parameter for search
    - Open settings window for action(setting)
    - _Requirements: 1.1, 2.1_
  - [ ] 7.3 Integrate all components
    - Call PixivApiService for data
    - Open winPixivPreview with results
    - Return appropriate response to LLM
    - _Requirements: 1.3, 2.3_

- [ ] 8. Final Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.
