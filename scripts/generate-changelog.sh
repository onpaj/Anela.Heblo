#!/bin/bash

# Changelog Generation Script for Anela.Heblo
# Generates changelog.json from git tags, commits, and GitHub issues

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUTPUT_FILE="$REPO_ROOT/frontend/public/changelog.json"
TRANSLATION_FILE="$SCRIPT_DIR/translation-mappings.json"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if jq is installed
check_dependencies() {
    if ! command -v jq &> /dev/null; then
        log_error "jq is required but not installed. Please install jq first."
        exit 1
    fi
    
    if ! command -v curl &> /dev/null; then
        log_error "curl is required but not installed. Please install curl first."
        exit 1
    fi
}

# Load translation mappings (kept for backward compatibility but not used)
load_translations() {
    if [[ ! -f "$TRANSLATION_FILE" ]]; then
        log_warning "Translation mappings file not found: $TRANSLATION_FILE (not needed - translation done by OpenAI)"
        return 0
    fi
    
    TRANSLATIONS=$(cat "$TRANSLATION_FILE")
    log_info "Translation mappings file found (translation will be done by OpenAI)"
}


# Get git tags sorted by version (semantic versioning)
get_git_tags() {
    git tag --sort=-version:refname | head -6
}

# Get current version (latest tag or 0.1.0 if no tags)
get_current_version() {
    local latest_tag=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
    if [[ -z "$latest_tag" ]]; then
        echo "0.1.0"
    else
        echo "$latest_tag"
    fi
}

# Check if commit should be excluded based on patterns
should_exclude_commit() {
    local message="$1"
    local exclude_patterns=(
        "merge"
        "bump"
        "update dependencies"
        "fix typo"
        "formatting"
        "lint"
        "^Merge pull request"
        "^Merge branch"
        "version bump"
        "update package"
        "node_modules"
        "gitignore"
        "readme"
        "\.md$"
    )
    
    for pattern in "${exclude_patterns[@]}"; do
        if echo "$message" | grep -qi "$pattern"; then
            return 0  # true - should exclude
        fi
    done
    
    return 1  # false - should include
}

# Parse conventional commit
parse_conventional_commit() {
    local message="$1"
    local hash="$2"
    
    # Check if it's a conventional commit
    if [[ $message =~ ^(feat|fix|docs|perf|refactor|test|chore|style|ci|build)(\(.+\))?:(.+)$ ]]; then
        local type="${BASH_REMATCH[1]}"
        local scope="${BASH_REMATCH[2]}"
        local description="${BASH_REMATCH[3]}"
        
        # Clean up description
        description=$(echo "$description" | sed 's/^[[:space:]]*//' | sed 's/[[:space:]]*$//')
        
        # Create JSON object (no translation - will be done by OpenAI)
        cat <<EOF
{
  "type": "$type",
  "title": "$description",
  "description": "$description",
  "source": "commit",
  "hash": "$hash"
}
EOF
    fi
}

# Fetch GitHub issues closed between versions
fetch_github_issues() {
    local from_tag="$1"
    local to_tag="$2"
    local from_date to_date
    
    if [[ -n "$from_tag" ]]; then
        from_date=$(git log -1 --format="%ai" "$from_tag" 2>/dev/null || echo "")
    fi
    
    if [[ -n "$to_tag" ]]; then
        to_date=$(git log -1 --format="%ai" "$to_tag" 2>/dev/null || echo "")
    else
        to_date=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    fi
    
    # Only fetch if we have GitHub token
    if [[ -z "$GITHUB_TOKEN" ]]; then
        log_warning "GITHUB_TOKEN not set, skipping GitHub issues"
        echo "[]"
        return
    fi
    
    # GitHub API query
    local query="repo:onpaj/Anela.Heblo is:issue is:closed"
    if [[ -n "$from_date" ]]; then
        query="$query closed:>$from_date"
    fi
    if [[ -n "$to_date" ]]; then
        query="$query closed:<$to_date"
    fi
    
    log_info "Fetching GitHub issues for period: ${from_date:-start} to ${to_date:-now}"
    
    # Skip GitHub API call if no token provided
    if [[ -z "$GITHUB_TOKEN" ]]; then
        log_warning "No GitHub token provided, skipping issue fetching"
        return 0
    fi
    
    local api_url="https://api.github.com/search/issues?q=$(echo "$query" | sed 's/ /%20/g')&per_page=100"
    log_info "API URL: $api_url"
    
    local response=$(curl -s -H "Authorization: token $GITHUB_TOKEN" \
        -H "Accept: application/vnd.github.v3+json" \
        "$api_url")
    
    # Check if response is valid JSON
    if ! echo "$response" | jq empty 2>/dev/null; then
        log_warning "Invalid JSON response from GitHub API:"
        log_warning "Response: $(echo "$response" | head -3)"
        return 0
    fi
    
    # Check for API errors
    if echo "$response" | jq -e '.message' >/dev/null 2>&1; then
        local error_msg=$(echo "$response" | jq -r '.message')
        log_warning "GitHub API error: $error_msg"
        return 0
    fi
    
    # Parse issues and convert to changelog format (no translation - will be done by OpenAI)
    echo "$response" | jq -r '.items[]? | select(.labels[]?.name | test("enhancement|bug|feature|improvement")) | 
    {
        type: (if (.labels[]?.name | test("bug")) then "fix" 
               elif (.labels[]?.name | test("enhancement|improvement")) then "improvement"
               else "feature" end),
        title: .title,
        description: (.body // .title | split("\n")[0] | .[0:100]),
        source: "github-issue",
        id: ("#" + (.number | tostring))
    }' 2>/dev/null || true
}

# Generate changelog for a specific version (simplified)
generate_version_changelog() {
    local version="$1"
    local prev_version="$2"
    local next_version="$3"
    
    # Log to stderr so it doesn't interfere with JSON output
    echo -e "${BLUE}[INFO]${NC} Generating changelog for version $version" >&2
    
    # Clean version number
    local clean_version=${version#v}
    
    # Get version date
    local version_date
    if git rev-parse "$version" >/dev/null 2>&1; then
        version_date=$(git log -1 --date=format:%Y-%m-%d --format=%ad "$version" 2>/dev/null || date +"%Y-%m-%d")
    else
        version_date=$(date +"%Y-%m-%d")
    fi
    
    # Create simple version object with basic info (no translation - will be done by OpenAI)
    cat <<EOF
{
  "version": "$clean_version",
  "date": "$version_date",
  "changes": [
    {
      "type": "feature",
      "title": "Version $clean_version released",
      "description": "New version of the application. Details can be found in GitHub releases.",
      "source": "system",
      "id": "$version"
    }
  ]
}
EOF
}

# Main function to generate complete changelog
generate_changelog() {
    log_info "Starting changelog generation..."
    
    cd "$REPO_ROOT"
    
    # Get current version and available tags
    local current_version=$(get_current_version)
    local clean_current=${current_version#v}
    local tags=($(get_git_tags))
    
    log_info "Current version: $current_version"
    log_info "Available tags: ${tags[*]}"
    
    # Start building changelog JSON
    local versions="["
    local first=true
    
    # Process up to 6 versions (current + 5 previous)
    local processed=0
    local prev_tag=""
    
    for tag in "${tags[@]}"; do
        if [[ $processed -ge 6 ]]; then
            break
        fi
        
        log_info "Processing tag: $tag (iteration $processed)"
        
        if [[ "$first" == true ]]; then
            first=false
        else
            versions="$versions,"
        fi
        
        local version_data
        version_data=$(generate_version_changelog "$tag" "$prev_tag" "")
        if [[ -n "$version_data" ]]; then
            # Validate that version_data is valid JSON
            if echo "$version_data" | jq . >/dev/null 2>&1; then
                versions="$versions$version_data"
                log_info "Added version data for $tag"
            else
                log_error "Generated invalid JSON for $tag: $version_data"
                exit 1
            fi
        else
            log_error "Failed to generate data for $tag"
            exit 1
        fi
        
        prev_tag="$tag"
        ((processed++))
    done
    
    # If no tags exist, create a default version
    if [[ $processed -eq 0 ]]; then
        local default_version=$(generate_version_changelog "$current_version" "" "")
        versions="$versions$default_version"
    fi
    
    versions="$versions]"
    
    # Create final changelog JSON
    local changelog=$(cat <<EOF
{
  "currentVersion": "$clean_current",
  "lastUpdated": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "versions": $versions
}
EOF
)
    
    # Debug: Show generated JSON
    log_info "Generated JSON length: ${#changelog} characters"
    
    # Validate JSON and write to file
    if ! echo "$changelog" | jq . > "$OUTPUT_FILE" 2>/dev/null; then
        log_error "Failed to generate valid JSON, creating minimal changelog"
        log_error "JSON validation error - first 200 chars: $(echo "$changelog" | head -c 200)"
        cat > "$OUTPUT_FILE" <<EOF
{
  "currentVersion": "$clean_current",
  "lastUpdated": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "versions": [
    {
      "version": "$clean_current",
      "date": "$(date +"%Y-%m-%d")",
      "changes": [
        {
          "type": "info",
          "title": "Changelog generation failed",
          "description": "For more information visit GitHub repository",
          "source": "system",
          "hash": "fallback"
        }
      ]
    }
  ]
}
EOF
    fi
    
    if [[ $? -eq 0 ]]; then
        log_success "Changelog generated successfully: $OUTPUT_FILE"
        
        # Try to get version count from the generated file, but don't fail if jq has issues
        local version_count
        version_count=$(jq -r '.versions | length' "$OUTPUT_FILE" 2>/dev/null || echo "unknown")
        
        if [[ "$version_count" != "unknown" ]]; then
            log_info "Changelog contains $version_count versions"
        fi
        
        log_success "Changelog generation complete!"
    else
        log_error "Failed to generate valid JSON changelog"
        exit 1
    fi
}

# Main script execution
main() {
    log_info "Anela.Heblo Changelog Generator"
    log_info "==============================="
    
    check_dependencies
    load_translations
    
    # Create output directory if it doesn't exist
    mkdir -p "$(dirname "$OUTPUT_FILE")"
    
    generate_changelog
    
    log_success "Changelog generation complete!"
}

# Run main function
main "$@"