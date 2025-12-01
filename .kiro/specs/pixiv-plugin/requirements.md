# Requirements Document

## Introduction

本文档定义了 VPetLLM Pixiv 插件的功能需求。该插件允许用户通过 LLM 调用搜索 Pixiv 图片或查看排行榜，并在弹出窗口中预览和下载图片。插件需要处理 Pixiv 的防盗链机制，并支持代理设置以确保图片能够正常加载。

## Glossary

- **Pixiv_Plugin**: VPetLLM 的 Pixiv 图片搜索和浏览插件
- **Pixiv_API**: 第三方 Pixiv API 服务 (https://ai.ycxom.top:6523/api)
- **Thumbnail_Image**: 缩略图，使用 image_urls.large 字段的图片 URL
- **Original_Image**: 原图，使用 meta_single_page.original_image_url 或 meta_pages[].image_urls.original 字段的图片 URL
- **Referer_Header**: HTTP 请求头，用于绕过 Pixiv 防盗链机制，值为 "https://www.pixiv.net/"
- **Illust**: Pixiv 插画作品数据对象
- **Ranking_Mode**: 排行榜模式，支持 day(日榜)、week(周榜)、month(月榜) 等
- **Offset**: 分页偏移量，每页 30 条数据

## Requirements

### Requirement 1

**User Story:** As a user, I want to search for images on Pixiv by keyword, so that I can find artwork related to my interests.

#### Acceptance Criteria

1. WHEN a user provides a search keyword THEN the Pixiv_Plugin SHALL send a GET request to Pixiv_API search endpoint with the keyword parameter
2. WHEN the Pixiv_API returns search results THEN the Pixiv_Plugin SHALL parse the JSON response and extract Illust objects
3. WHEN search results are available THEN the Pixiv_Plugin SHALL immediately display a preview window showing the first Thumbnail_Image
4. IF the search keyword is empty or whitespace THEN the Pixiv_Plugin SHALL return an error message without making an API request
5. IF the Pixiv_API returns an error or empty results THEN the Pixiv_Plugin SHALL display an appropriate error message to the user

### Requirement 2

**User Story:** As a user, I want to get a random beautiful image from Pixiv ranking, so that I can discover popular artwork easily.

#### Acceptance Criteria

1. WHEN a user requests a random image THEN the Pixiv_Plugin SHALL send a GET request to Pixiv_API ranking endpoint with day mode
2. WHEN the Pixiv_API returns ranking results THEN the Pixiv_Plugin SHALL randomly select one Illust from the results
3. WHEN a random image is selected THEN the Pixiv_Plugin SHALL immediately display a preview window showing the Thumbnail_Image
4. IF the Pixiv_API returns an error or empty results THEN the Pixiv_Plugin SHALL display an appropriate error message to the user

### Requirement 3

**User Story:** As a user, I want to navigate through search results in the preview window, so that I can browse images I searched for.

#### Acceptance Criteria

1. WHEN the preview window displays search results THEN the Pixiv_Plugin SHALL show navigation buttons for previous and next images
2. WHEN a user clicks the next button THEN the Pixiv_Plugin SHALL display the next Thumbnail_Image in the search result list
3. WHEN a user clicks the previous button THEN the Pixiv_Plugin SHALL display the previous Thumbnail_Image in the search result list
4. WHEN the user is at the first image THEN the Pixiv_Plugin SHALL disable the previous button
5. WHEN the user is at the last image THEN the Pixiv_Plugin SHALL disable the next button
6. WHEN displaying an image THEN the Pixiv_Plugin SHALL show the current image index and total count

### Requirement 4

**User Story:** As a user, I want to download the original high-resolution image, so that I can save artwork I like.

#### Acceptance Criteria

1. WHEN a user clicks the download button THEN the Pixiv_Plugin SHALL download the Original_Image using the correct URL
2. WHEN downloading an image THEN the Pixiv_Plugin SHALL include the Referer_Header to bypass Pixiv anti-hotlinking
3. WHEN the download completes THEN the Pixiv_Plugin SHALL save the image to a user-accessible location
4. WHEN downloading THEN the Pixiv_Plugin SHALL show download progress to the user
5. IF the download fails THEN the Pixiv_Plugin SHALL display an error message and allow retry

### Requirement 5

**User Story:** As a user, I want images to load correctly despite Pixiv's anti-hotlinking protection, so that I can view all images without errors.

#### Acceptance Criteria

1. WHEN loading a Thumbnail_Image for preview THEN the Pixiv_Plugin SHALL include the Referer_Header in the HTTP request
2. WHEN loading an Original_Image for download THEN the Pixiv_Plugin SHALL include the Referer_Header in the HTTP request
3. WHEN an image fails to load due to anti-hotlinking THEN the Pixiv_Plugin SHALL retry with proper headers
4. WHEN proxy is configured for image loading THEN the Pixiv_Plugin SHALL route image requests through the configured proxy

### Requirement 6

**User Story:** As a user, I want to configure proxy settings for image loading, so that I can access images even when direct connections are blocked.

#### Acceptance Criteria

1. WHEN a user opens the settings window THEN the Pixiv_Plugin SHALL display proxy configuration options
2. WHEN proxy settings are configured THEN the Pixiv_Plugin SHALL apply proxy only to image download and preview requests
3. WHEN "follow VPetLLM proxy" option is selected THEN the Pixiv_Plugin SHALL use the same proxy settings as VPetLLM
4. WHEN proxy settings are saved THEN the Pixiv_Plugin SHALL persist the settings to a configuration file
5. THE Pixiv_Plugin SHALL NOT apply proxy to Pixiv_API requests since the API is not blocked by GFW

### Requirement 7

**User Story:** As a user, I want to see image metadata in the preview window, so that I can learn more about the artwork.

#### Acceptance Criteria

1. WHEN displaying an image THEN the Pixiv_Plugin SHALL show the image title
2. WHEN displaying an image THEN the Pixiv_Plugin SHALL show the author name
3. WHEN displaying an image THEN the Pixiv_Plugin SHALL show the image tags
4. WHEN displaying an image with multiple pages THEN the Pixiv_Plugin SHALL indicate the page count and allow navigation between pages

### Requirement 8

**User Story:** As a user, I want the plugin to handle multi-page illustrations correctly, so that I can view all pages of an artwork.

#### Acceptance Criteria

1. WHEN an Illust has page_count greater than 1 THEN the Pixiv_Plugin SHALL use meta_pages array for image URLs
2. WHEN viewing a multi-page Illust THEN the Pixiv_Plugin SHALL allow navigation between pages within the same artwork
3. WHEN downloading a multi-page Illust THEN the Pixiv_Plugin SHALL download the currently displayed page's Original_Image
4. WHEN displaying a multi-page Illust THEN the Pixiv_Plugin SHALL show the current page number and total page count
