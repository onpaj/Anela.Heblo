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


# Get git tags sorted by version (semantic versioning) - get last 5 existing tags
get_git_tags() {
    git tag --sort=-version:refname | head -5
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

# Generate "current" version changelog from commits since last tag
generate_current_version_changelog() {
    local latest_tag=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
    local version_date=$(date +"%Y-%m-%d")
    
    # Determine current version from GitVersion environment variables or fallback
    local current_version="current"
    
    # Check for GitVersion environment variables (used in CI/CD)
    if [[ -n "$GITVERSION_FULLSEMVER" ]]; then
        current_version="$GITVERSION_FULLSEMVER"
        log_info "Using GitVersion FULLSEMVER: $current_version" >&2
    elif [[ -n "$GITVERSION_SEMVER" ]]; then
        current_version="$GITVERSION_SEMVER"
        log_info "Using GitVersion SEMVER: $current_version" >&2
    elif [[ -n "$GITVERSION_MAJORMINORPATCH" ]]; then
        current_version="$GITVERSION_MAJORMINORPATCH"
        log_info "Using GitVersion MAJORMINORPATCH: $current_version" >&2
    else
        # Try to run dotnet gitversion if available
        if command -v dotnet >/dev/null 2>&1; then
            local gitversion_output
            gitversion_output=$(dotnet gitversion --output json 2>/dev/null || echo "")
            if [[ -n "$gitversion_output" ]] && echo "$gitversion_output" | jq empty 2>/dev/null; then
                current_version=$(echo "$gitversion_output" | jq -r '.FullSemVer // .SemVer // .MajorMinorPatch // "current"' 2>/dev/null || echo "current")
                log_info "Using dotnet gitversion: $current_version" >&2
            else
                log_info "GitVersion not available, using fallback version: $current_version" >&2
            fi
        else
            log_info "GitVersion not available, using fallback version: $current_version" >&2
        fi
    fi
    
    echo -e "${BLUE}[INFO]${NC} Generating current branch changelog since $latest_tag (version: $current_version)" >&2
    
    # Get commits since last tag
    local commit_range=""
    if [[ -n "$latest_tag" ]]; then
        commit_range="$latest_tag..HEAD"
    else
        # If no tags, get last 10 commits
        commit_range="HEAD~10..HEAD"
    fi
    
    echo -e "${BLUE}[DEBUG]${NC} Getting commits in range: $commit_range" >&2
    
    # Get commits and parse them
    local changes="["
    local first_change=true
    
    # Get commits from git
    while IFS= read -r line; do
        if [[ -n "$line" ]]; then
            local hash=$(echo "$line" | cut -d'|' -f1)
            local message=$(echo "$line" | cut -d'|' -f2-)
            
            # Skip if should be excluded
            if should_exclude_commit "$message"; then
                continue
            fi
            
            # Parse conventional commit or create generic entry
            local commit_json=$(parse_conventional_commit "$message" "$hash")
            
            if [[ -n "$commit_json" ]]; then
                if [[ "$first_change" == false ]]; then
                    changes="$changes,"
                fi
                changes="$changes$commit_json"
                first_change=false
            fi
        fi
    done < <(git log --format="%h|%s" "$commit_range" 2>/dev/null || true)
    
    changes="$changes]"
    
    # Only return JSON if there are actual changes
    if [[ "$first_change" == false ]]; then
        cat <<EOF
{
  "version": "$current_version",
  "date": "$version_date",
  "changes": $changes
}
EOF
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
        
        # Normalize feat to feature
        if [[ "$type" == "feat" ]]; then
            type="feature"
        fi
        
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

# Generate changelog for a specific version
generate_version_changelog() {
    local version="$1"
    local prev_version="$2"
    local next_version="$3"
    
    # Log to stderr so it doesn't interfere with JSON output
    echo -e "${BLUE}[INFO]${NC} Generating changelog for version $version" >&2
    echo -e "${BLUE}[DEBUG]${NC} Input parameters: version=$version, prev_version=$prev_version, next_version=$next_version" >&2
    
    # Clean version number
    local clean_version=${version#v}
    echo -e "${BLUE}[DEBUG]${NC} Clean version: $clean_version" >&2
    
    # Get version date
    local version_date
    version_date=$(date +"%Y-%m-%d")
    
    # Try to get actual tag date if possible
    set +e
    if git show --format=%cd --date=format:%Y-%m-%d -s "$version" >/dev/null 2>&1; then
        version_date=$(git show --format=%cd --date=format:%Y-%m-%d -s "$version" 2>/dev/null || date +"%Y-%m-%d")
    fi
    set -e
    
    echo -e "${BLUE}[DEBUG]${NC} Final date: $version_date" >&2
    
    # Get commits between versions
    local commit_range=""
    if [[ -n "$prev_version" ]]; then
        commit_range="$prev_version..$version"
    else
        # For the first version, get last 10 commits
        commit_range="$version~10..$version"
    fi
    
    echo -e "${BLUE}[DEBUG]${NC} Getting commits in range: $commit_range" >&2
    
    # Get commits and parse them
    local changes="["
    local first_change=true
    
    # Get commits from git
    while IFS= read -r line; do
        if [[ -n "$line" ]]; then
            local hash=$(echo "$line" | cut -d'|' -f1)
            local message=$(echo "$line" | cut -d'|' -f2-)
            
            # Skip if should be excluded
            if should_exclude_commit "$message"; then
                continue
            fi
            
            # Parse conventional commit or create generic entry
            local commit_json=$(parse_conventional_commit "$message" "$hash")
            
            if [[ -n "$commit_json" ]]; then
                if [[ "$first_change" == false ]]; then
                    changes="$changes,"
                fi
                changes="$changes$commit_json"
                first_change=false
            fi
        fi
    done < <(git log --format="%h|%s" "$commit_range" 2>/dev/null || true)
    
    # Get GitHub issues for this version range
    local issues=$(fetch_github_issues "$prev_version" "$version")
    if [[ -n "$issues" && "$issues" != "[]" ]]; then
        echo -e "${BLUE}[DEBUG]${NC} Found GitHub issues for $version" >&2
        # Add issues to changes
        while IFS= read -r issue; do
            if [[ -n "$issue" && "$issue" != "null" ]]; then
                if [[ "$first_change" == false ]]; then
                    changes="$changes,"
                fi
                changes="$changes$issue"
                first_change=false
            fi
        done < <(echo "$issues" | jq -c '.[]?' 2>/dev/null || true)
    fi
    
    # If no changes found, don't add anything (empty changes array)
    # System-generated entries are not useful in changelog
    
    changes="$changes]"
    
    # Create version object
    cat <<EOF
{
  "version": "$clean_version",
  "date": "$version_date",
  "changes": $changes
}
EOF
    
    echo -e "${BLUE}[DEBUG]${NC} JSON object generated successfully" >&2
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
    
    # First, check if there are changes since last tag and add current version
    local current_changelog=$(generate_current_version_changelog)
    if [[ -n "$current_changelog" ]]; then
        log_info "Found changes since last tag, adding current version"
        versions="$versions$current_changelog"
        first=false
    fi
    
    # Process the existing tags (up to 5)
    local processed=0
    local prev_tag=""
    
    # Create array of tags for easier navigation
    local tags_array=(${tags[@]})
    
    for i in "${!tags_array[@]}"; do
        if [[ $processed -ge 5 ]]; then
            break
        fi
        
        local tag="${tags_array[$i]}"
        
        # Get previous tag (older version)
        if [[ $((i + 1)) -lt ${#tags_array[@]} ]]; then
            prev_tag="${tags_array[$((i + 1))]}"
        else
            prev_tag=""
        fi
        
        log_info "Processing tag: $tag (iteration $processed, prev: $prev_tag)"
        
        if [[ "$first" == true ]]; then
            first=false
        else
            versions="$versions,"
        fi
        
        log_info "About to call generate_version_changelog for $tag"
        
        local version_data
        # Generate version data
        set +e  # Temporarily disable exit on error
        version_data=$(generate_version_changelog "$tag" "$prev_tag" "")
        local gen_exit_code=$?
        set -e  # Re-enable exit on error
        
        log_info "generate_version_changelog returned exit code: $gen_exit_code"
        log_info "Length of generated data: ${#version_data}"
        
        if [[ $gen_exit_code -eq 0 && -n "$version_data" ]]; then
            # Show first 100 chars of generated data
            log_info "Generated data preview: ${version_data:0:100}..."
            
            # Validate that version_data is valid JSON
            log_info "Validating JSON..."
            if echo "$version_data" | jq . >/dev/null 2>&1; then
                log_info "JSON validation successful"
                versions="$versions$version_data"
                log_info "Added version data for $tag"
                log_info "Current versions string length: ${#versions}"
            else
                log_error "Generated invalid JSON for $tag"
                log_error "JSON validation failed. Data: $version_data"
                exit 1
            fi
        else
            log_error "Failed to generate data for $tag (exit code: $gen_exit_code)"
            if [[ -n "$version_data" ]]; then
                log_error "Output was: $version_data"
            fi
            exit 1
        fi
        
        processed=$((processed + 1))
        log_info "Completed processing $tag, moving to next iteration"
    done
    
    # If no tags exist and no current changes, create a default version
    if [[ $processed -eq 0 && "$first" == true ]]; then
        local default_version=$(generate_version_changelog "$current_version" "" "")
        versions="$versions$default_version"
    fi
    
    versions="$versions]"
    
    # Determine current version for JSON output using GitVersion if available
    local json_current_version="$clean_current"
    
    # Check for GitVersion environment variables for currentVersion field
    if [[ -n "$GITVERSION_FULLSEMVER" ]]; then
        json_current_version="$GITVERSION_FULLSEMVER"
        log_info "Using GitVersion FULLSEMVER for currentVersion: $json_current_version" >&2
    elif [[ -n "$GITVERSION_SEMVER" ]]; then
        json_current_version="$GITVERSION_SEMVER"
        log_info "Using GitVersion SEMVER for currentVersion: $json_current_version" >&2
    elif [[ -n "$GITVERSION_MAJORMINORPATCH" ]]; then
        json_current_version="$GITVERSION_MAJORMINORPATCH"
        log_info "Using GitVersion MAJORMINORPATCH for currentVersion: $json_current_version" >&2
    else
        # Try to run dotnet gitversion if available
        if command -v dotnet >/dev/null 2>&1; then
            local gitversion_output
            gitversion_output=$(dotnet gitversion --output json 2>/dev/null || echo "")
            if [[ -n "$gitversion_output" ]] && echo "$gitversion_output" | jq empty 2>/dev/null; then
                json_current_version=$(echo "$gitversion_output" | jq -r '.FullSemVer // .SemVer // .MajorMinorPatch // "'"$clean_current"'"' 2>/dev/null || echo "$clean_current")
                log_info "Using dotnet gitversion for currentVersion: $json_current_version" >&2
            else
                log_info "GitVersion not available, using tag-based currentVersion: $json_current_version" >&2
            fi
        else
            log_info "GitVersion not available, using tag-based currentVersion: $json_current_version" >&2
        fi
    fi
    
    # Create final changelog JSON
    local changelog=$(cat <<EOF
{
  "currentVersion": "$json_current_version",
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