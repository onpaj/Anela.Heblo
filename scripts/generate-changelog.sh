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

# Load translation mappings
load_translations() {
    if [[ ! -f "$TRANSLATION_FILE" ]]; then
        log_error "Translation mappings file not found: $TRANSLATION_FILE"
        exit 1
    fi
    
    TRANSLATIONS=$(cat "$TRANSLATION_FILE")
    log_info "Loaded translation mappings"
}

# Translate English text to Czech using mappings
translate_text() {
    local text="$1"
    local translated="$text"
    
    # Apply translation mappings
    while IFS='=' read -r key value; do
        # Remove quotes from jq output
        key=$(echo "$key" | sed 's/"//g')
        value=$(echo "$value" | sed 's/"//g')
        translated=$(echo "$translated" | sed -i.bak "s/\\b$key\\b/$value/gi" 2>/dev/null && echo "$translated" || echo "$translated")
    done < <(echo "$TRANSLATIONS" | jq -r 'to_entries[] | "\(.key)=\(.value)"')
    
    echo "$translated"
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
        
        # Translate type and description
        local translated_type=$(translate_text "$type")
        local translated_desc=$(translate_text "$description")
        
        # Create JSON object
        cat <<EOF
{
  "type": "$translated_type",
  "title": "$translated_desc",
  "description": "$translated_desc",
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
    
    local response=$(curl -s -H "Authorization: token $GITHUB_TOKEN" \
        -H "Accept: application/vnd.github.v3+json" \
        "https://api.github.com/search/issues?q=$(echo "$query" | sed 's/ /%20/g')&per_page=100")
    
    # Parse issues and convert to changelog format
    echo "$response" | jq -r '.items[]? | select(.labels[]?.name | test("enhancement|bug|feature|improvement")) | 
    {
        type: (if (.labels[]?.name | test("bug")) then "oprava" 
               elif (.labels[]?.name | test("enhancement|improvement")) then "vylepšení"
               else "funkcionalita" end),
        title: .title,
        description: (.body // .title | split("\n")[0] | .[0:100]),
        source: "github-issue",
        id: ("#" + (.number | tostring))
    }'
}

# Generate changelog for a specific version
generate_version_changelog() {
    local version="$1"
    local prev_version="$2"
    local next_version="$3"
    
    log_info "Generating changelog for version $version"
    
    # Get version date
    local version_date
    if git rev-parse "$version" >/dev/null 2>&1; then
        version_date=$(git log -1 --format="%Y-%m-%d" "$version")
    else
        version_date=$(date +"%Y-%m-%d")
    fi
    
    # Get commits between versions
    local commit_range
    if [[ -n "$prev_version" ]] && git rev-parse "$prev_version" >/dev/null 2>&1; then
        commit_range="$prev_version..$version"
    else
        # For first version, get all commits
        commit_range="$version"
    fi
    
    log_info "Processing commits in range: $commit_range"
    
    # Collect commit changes
    local commit_changes=()
    while IFS= read -r line; do
        local hash=$(echo "$line" | cut -d' ' -f1)
        local message=$(echo "$line" | cut -d' ' -f2-)
        
        # Skip excluded commits
        if should_exclude_commit "$message"; then
            continue
        fi
        
        # Parse conventional commit
        local change=$(parse_conventional_commit "$message" "$hash")
        if [[ -n "$change" ]]; then
            commit_changes+=("$change")
        fi
    done < <(git log --format="%h %s" "$commit_range" 2>/dev/null || true)
    
    # Fetch GitHub issues
    local issue_changes
    issue_changes=$(fetch_github_issues "$prev_version" "$next_version")
    
    # Combine all changes
    local all_changes="["
    local first=true
    
    # Add commit changes
    for change in "${commit_changes[@]}"; do
        if [[ "$first" == true ]]; then
            first=false
        else
            all_changes="$all_changes,"
        fi
        all_changes="$all_changes$change"
    done
    
    # Add issue changes
    if [[ "$issue_changes" != "[]" && -n "$issue_changes" ]]; then
        echo "$issue_changes" | jq -c '.[]?' | while read -r issue; do
            if [[ "$first" == true ]]; then
                first=false
            else
                all_changes="$all_changes,"
            fi
            all_changes="$all_changes$issue"
        done
    fi
    
    all_changes="$all_changes]"
    
    # Create version object
    cat <<EOF
{
  "version": "$version",
  "date": "$version_date",
  "changes": $all_changes
}
EOF
}

# Main function to generate complete changelog
generate_changelog() {
    log_info "Starting changelog generation..."
    
    cd "$REPO_ROOT"
    
    # Get current version and available tags
    local current_version=$(get_current_version)
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
        
        if [[ "$first" == true ]]; then
            first=false
        else
            versions="$versions,"
        fi
        
        local version_data=$(generate_version_changelog "$tag" "$prev_tag" "")
        versions="$versions$version_data"
        
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
  "currentVersion": "$current_version",
  "versions": $versions
}
EOF
)
    
    # Validate JSON and write to file
    echo "$changelog" | jq . > "$OUTPUT_FILE"
    
    if [[ $? -eq 0 ]]; then
        log_success "Changelog generated successfully: $OUTPUT_FILE"
        log_info "Changelog contains $(echo "$changelog" | jq '.versions | length') versions"
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