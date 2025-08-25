#!/bin/bash

# Database Migration Dry-Run Script
# Shows what objects will change when applying Entity Framework migrations

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_header() {
    echo -e "\n${BLUE}=== $1 ===${NC}\n"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

# Check if we're in the correct directory
if [ ! -f "backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj" ]; then
    print_error "Please run this script from the project root directory"
    exit 1
fi

# Change to the API project directory
cd backend/src/Anela.Heblo.API

print_header "Database Migration Dry-Run Analysis"

# Get database connection info
print_header "Database Connection Information"

# Function to extract database info from connection string
extract_db_info() {
    local conn_string="$1"
    local source="$2"
    
    if [ -n "$conn_string" ]; then
        # Extract database name from PostgreSQL connection string
        DB_NAME=$(echo "$conn_string" | grep -o "Database=[^;]*" | cut -d'=' -f2 || echo "")
        if [ -z "$DB_NAME" ]; then
            # Try alternative format (URI style)
            DB_NAME=$(echo "$conn_string" | grep -o "/[^?;]*" | tail -1 | sed 's/^.//' || echo "")
        fi
        
        # Extract server/host info
        SERVER_INFO=$(echo "$conn_string" | grep -o "Host=[^;]*\|Server=[^;]*" | head -1 | cut -d'=' -f2 || echo "")
        if [ -z "$SERVER_INFO" ]; then
            # Try URI format
            SERVER_INFO=$(echo "$conn_string" | grep -o "://[^@]*@[^:/]*" | sed 's/.*@//' || echo "")
            if [ -z "$SERVER_INFO" ]; then
                SERVER_INFO=$(echo "$conn_string" | grep -o "://[^:/]*" | sed 's/^:\/\///' || echo "")
            fi
        fi
        
        if [ -n "$DB_NAME" ] && [ -n "$SERVER_INFO" ]; then
            print_success "Target Database: $DB_NAME @ $SERVER_INFO ($source)"
            return 0
        elif [ -n "$DB_NAME" ]; then
            print_success "Target Database: $DB_NAME ($source)"
            return 0
        fi
    fi
    return 1
}

# Primary: Try to get from user secrets (most reliable for local development)
USER_SECRETS_ID="f4e6382a-aefd-47ef-9cd7-7e12daac7e45"
SECRETS_PATH=""

# Determine user secrets path based on OS
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    SECRETS_PATH="$HOME/.microsoft/usersecrets/$USER_SECRETS_ID/secrets.json"
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    # Linux
    SECRETS_PATH="$HOME/.microsoft/usersecrets/$USER_SECRETS_ID/secrets.json"
elif [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]] || [[ "$OSTYPE" == "win32" ]]; then
    # Windows
    SECRETS_PATH="$APPDATA/Microsoft/UserSecrets/$USER_SECRETS_ID/secrets.json"
fi

CONNECTION_STRING=""
if [ -f "$SECRETS_PATH" ]; then
    print_success "User secrets file found at: $SECRETS_PATH"
    
    # Check if file is readable
    if [ ! -r "$SECRETS_PATH" ]; then
        print_error "Secrets file is not readable (permission issue)"
        return 1
    fi
    
    # Check file size
    FILE_SIZE=$(wc -c < "$SECRETS_PATH" 2>/dev/null || echo "0")
    echo "File size: $FILE_SIZE bytes"
    
    if [ "$FILE_SIZE" -eq 0 ]; then
        print_warning "Secrets file is empty"
        return 1
    fi
    
    # Show raw file content (for debugging - will mask connection strings)
    echo "Raw file content:"
    echo "----------------------------------------"
    cat "$SECRETS_PATH" 2>/dev/null | sed 's/\(Host\|Server\|Password\|Uid\|User Id\)=[^;]*/\1=***MASKED***/g' || echo "(Unable to read file)"
    echo "----------------------------------------"
    
    # Test if it's valid JSON
    if ! jq empty "$SECRETS_PATH" 2>/dev/null; then
        print_error "Secrets file contains invalid JSON"
        return 1
    fi
    
    # Debug: Show the structure of the secrets file (without sensitive values)
    echo "Secrets file structure:"
    jq -r 'keys[]' "$SECRETS_PATH" 2>/dev/null | sed 's/^/  - /' || echo "  (Unable to parse JSON keys)"
    
    # If ConnectionStrings exists, show its nested keys
    if jq -e '.ConnectionStrings' "$SECRETS_PATH" >/dev/null 2>&1; then
        echo "ConnectionStrings keys:"
        jq -r '.ConnectionStrings | keys[]' "$SECRETS_PATH" 2>/dev/null | sed 's/^/  - /' || echo "  (Unable to parse ConnectionStrings keys)"
    else
        echo "No ConnectionStrings object found"
    fi
    
    echo ""
    
    # Try the nested JSON format: ConnectionStrings.Default
    CONNECTION_STRING=$(jq -r '.ConnectionStrings.Default // empty' "$SECRETS_PATH" 2>/dev/null || echo "")
    if [ -n "$CONNECTION_STRING" ] && [ "$CONNECTION_STRING" != "null" ] && [ "$CONNECTION_STRING" != "empty" ]; then
        print_success "Found connection string with key: ConnectionStrings.Default"
    else
        # Fallback to double underscore format: ConnectionStrings__Default
        CONNECTION_STRING=$(jq -r '.ConnectionStrings__Default // empty' "$SECRETS_PATH" 2>/dev/null || echo "")
        if [ -n "$CONNECTION_STRING" ] && [ "$CONNECTION_STRING" != "null" ] && [ "$CONNECTION_STRING" != "empty" ]; then
            print_success "Found connection string with key: ConnectionStrings__Default"
        else
            # Final fallback to DefaultConnection: ConnectionStrings.DefaultConnection
            CONNECTION_STRING=$(jq -r '.ConnectionStrings.DefaultConnection // empty' "$SECRETS_PATH" 2>/dev/null || echo "")
            if [ -n "$CONNECTION_STRING" ] && [ "$CONNECTION_STRING" != "null" ] && [ "$CONNECTION_STRING" != "empty" ]; then
                print_success "Found connection string with key: ConnectionStrings.DefaultConnection"
            fi
        fi
    fi
    
    if extract_db_info "$CONNECTION_STRING" "user secrets"; then
        # Success - found database info in user secrets
        :
    else
        print_warning "User secrets file found but no valid connection string"
        echo "  Checked keys: ConnectionStrings.Default, ConnectionStrings__Default, ConnectionStrings.DefaultConnection"
        echo "  Connection string value: ${CONNECTION_STRING:0:50}..." # Show first 50 chars for debugging
    fi
else
    print_warning "User secrets file not found at: $SECRETS_PATH"
fi

# Secondary: Try EF Core dbcontext info if user secrets failed
if [ -z "$DB_NAME" ]; then
    CONNECTION_STRING=$(dotnet ef dbcontext info --json 2>/dev/null | jq -r '.connectionString' 2>/dev/null || echo "")
    if extract_db_info "$CONNECTION_STRING" "EF Core context"; then
        # Success - found database info from EF Core
        :
    fi
fi

# Tertiary: Try appsettings files if both above failed
if [ -z "$DB_NAME" ]; then
    for APPSETTINGS_FILE in "appsettings.Development.json" "appsettings.json"; do
        if [ -f "$APPSETTINGS_FILE" ]; then
            CONNECTION_STRING=$(jq -r '.ConnectionStrings.DefaultConnection // empty' "$APPSETTINGS_FILE" 2>/dev/null || echo "")
            if extract_db_info "$CONNECTION_STRING" "$APPSETTINGS_FILE"; then
                break
            fi
        fi
    done
fi

# Final fallback
if [ -z "$DB_NAME" ]; then
    print_error "Could not determine target database name from any source"
    echo "Checked sources:"
    echo "  1. User secrets: $SECRETS_PATH"
    echo "  2. EF Core context info"
    echo "  3. appsettings.Development.json"
    echo "  4. appsettings.json"
    echo ""
    print_warning "Continuing with migration analysis..."
fi

echo ""

# Check if there are pending migrations
print_header "Checking for Pending Migrations"

# Get list of applied migrations
APPLIED_MIGRATIONS=$(dotnet ef migrations list --json 2>/dev/null | jq -r '.[] | select(.applied == true) | .name' 2>/dev/null || echo "")
PENDING_MIGRATIONS=$(dotnet ef migrations list --json 2>/dev/null | jq -r '.[] | select(.applied == false) | .name' 2>/dev/null || echo "")

if [ -z "$PENDING_MIGRATIONS" ]; then
    print_success "No pending migrations found. Database is up to date."
    exit 0
fi

echo "Applied migrations:"
if [ -n "$APPLIED_MIGRATIONS" ]; then
    echo "$APPLIED_MIGRATIONS" | sed 's/^/  ✓ /'
else
    echo "  (none)"
fi

echo -e "\nPending migrations:"
echo "$PENDING_MIGRATIONS" | sed 's/^/  → /'

# Generate SQL script for pending migrations
print_header "Generating SQL Script for Pending Changes"

# Create temporary file for SQL output
TEMP_SQL_FILE=$(mktemp)
MIGRATION_SCRIPT_FILE="../../../migration-preview.sql"

# Generate SQL script
if dotnet ef migrations script --output "$TEMP_SQL_FILE" --idempotent 2>/dev/null; then
    # Copy to project root with better name
    cp "$TEMP_SQL_FILE" "$MIGRATION_SCRIPT_FILE"
    print_success "SQL script generated: migration-preview.sql"
else
    print_error "Failed to generate SQL script"
    rm -f "$TEMP_SQL_FILE"
    exit 1
fi

# Analyze the SQL script to show summary of changes
print_header "Analysis of Database Changes"

if [ -f "$MIGRATION_SCRIPT_FILE" ]; then
    # Count different types of operations
    CREATE_TABLES=$(grep -c "CREATE TABLE" "$MIGRATION_SCRIPT_FILE" 2>/dev/null || echo "0")
    ALTER_TABLES=$(grep -c "ALTER TABLE" "$MIGRATION_SCRIPT_FILE" 2>/dev/null || echo "0")
    DROP_TABLES=$(grep -c "DROP TABLE" "$MIGRATION_SCRIPT_FILE" 2>/dev/null || echo "0")
    CREATE_INDEXES=$(grep -c "CREATE.*INDEX" "$MIGRATION_SCRIPT_FILE" 2>/dev/null || echo "0")
    DROP_INDEXES=$(grep -c "DROP.*INDEX" "$MIGRATION_SCRIPT_FILE" 2>/dev/null || echo "0")
    
    echo "Summary of changes:"
    [ "$CREATE_TABLES" -gt 0 ] && echo "  📊 Tables to create: $CREATE_TABLES"
    [ "$ALTER_TABLES" -gt 0 ] && echo "  🔄 Tables to modify: $ALTER_TABLES"
    [ "$DROP_TABLES" -gt 0 ] && echo "  🗑️  Tables to drop: $DROP_TABLES"
    [ "$CREATE_INDEXES" -gt 0 ] && echo "  📈 Indexes to create: $CREATE_INDEXES"
    [ "$DROP_INDEXES" -gt 0 ] && echo "  📉 Indexes to drop: $DROP_INDEXES"
    
    # Show specific tables being affected
    print_header "Tables Being Modified"
    
    # Extract table names from CREATE TABLE statements
    if [ "$CREATE_TABLES" -gt 0 ]; then
        echo "New tables:"
        grep "CREATE TABLE" "$MIGRATION_SCRIPT_FILE" | sed 's/.*CREATE TABLE \[\([^]]*\)\].*/  + \1/' | sort -u
    fi
    
    # Extract table names from ALTER TABLE statements
    if [ "$ALTER_TABLES" -gt 0 ]; then
        echo "Modified tables:"
        grep "ALTER TABLE" "$MIGRATION_SCRIPT_FILE" | sed 's/.*ALTER TABLE \[\([^]]*\)\].*/  ~ \1/' | sort -u
    fi
    
    # Extract table names from DROP TABLE statements
    if [ "$DROP_TABLES" -gt 0 ]; then
        echo "Dropped tables:"
        grep "DROP TABLE" "$MIGRATION_SCRIPT_FILE" | sed 's/.*DROP TABLE \[\([^]]*\)\].*/  - \1/' | sort -u
    fi
    
    print_header "Generated SQL Script Preview"
    echo "Full SQL script saved to: migration-preview.sql"
    echo "Preview (first 50 lines):"
    echo "----------------------------------------"
    head -50 "$MIGRATION_SCRIPT_FILE" | sed 's/^/  /'
    
    TOTAL_LINES=$(wc -l < "$MIGRATION_SCRIPT_FILE")
    if [ "$TOTAL_LINES" -gt 50 ]; then
        echo "  ..."
        echo "  (and $(($TOTAL_LINES - 50)) more lines)"
    fi
    echo "----------------------------------------"
    
    print_header "How to Apply These Changes"
    echo "To apply these migrations to your database, run:"
    echo "  ${YELLOW}dotnet ef database update${NC}"
    echo ""
    echo "To apply migrations up to a specific migration, run:"
    echo "  ${YELLOW}dotnet ef database update <migration-name>${NC}"
    echo ""
    echo "To rollback to a previous migration, run:"
    echo "  ${YELLOW}dotnet ef database update <previous-migration-name>${NC}"
    
    print_warning "Always backup your database before applying migrations to production!"
    
else
    print_error "Could not analyze SQL script"
fi

# Cleanup
rm -f "$TEMP_SQL_FILE"

print_success "Dry-run analysis complete"