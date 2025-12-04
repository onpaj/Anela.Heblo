#!/bin/bash

# Default paths
PATH1="/Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend/src/Anela.Heblo.API/invoices"
PATH2="/Users/pajgrtondrej/Work/GitHub/Anela.Heblo.Blazor/src/Anela.Heblo.HttpApi.Host/invoices"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --path1)
            PATH1="$2"
            shift 2
            ;;
        --path2)
            PATH2="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [--path1 PATH] [--path2 PATH]"
            echo "  --path1   First directory to compare (current system)"
            echo "  --path2   Second directory to compare (reference system)"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

echo -e "${CYAN}=== INVOICE COMPARISON REPORT ===${NC}"
echo -e "${YELLOW}Path 1: $PATH1${NC}"
echo -e "${YELLOW}Path 2: $PATH2${NC}"
echo ""

# Check if directories exist
if [[ ! -d "$PATH1" ]]; then
    echo -e "${RED}ERROR: Path1 does not exist: $PATH1${NC}"
    exit 1
fi

if [[ ! -d "$PATH2" ]]; then
    echo -e "${RED}ERROR: Path2 does not exist: $PATH2${NC}"
    exit 1
fi

# Function to normalize content (remove DateCreated differences)
normalize_content() {
    local content="$1"
    # Remove DateCreated XML element completely and normalize line endings
    echo "$content" | sed 's|<DateCreated>.*</DateCreated>|<DateCreated>NORMALIZED</DateCreated>|g' | tr -d '\r'
}

# Get file lists
files1=($(find "$PATH1" -name "*.xml" -type f | sort))
files2=($(find "$PATH2" -name "*.xml" -type f | sort))

# Extract just filenames for comparison
names1=($(for f in "${files1[@]}"; do basename "$f"; done))
names2=($(for f in "${files2[@]}"; do basename "$f"; done))

# Find common files and files only in one directory
common_files=()
only_in_path1=()
only_in_path2=()

# Check files only in path1
for name in "${names1[@]}"; do
    if [[ ! " ${names2[@]} " =~ " ${name} " ]]; then
        only_in_path1+=("$name")
    fi
done

# Check files only in path2
for name in "${names2[@]}"; do
    if [[ ! " ${names1[@]} " =~ " ${name} " ]]; then
        only_in_path2+=("$name")
    fi
done

# Find common files
for name in "${names1[@]}"; do
    if [[ " ${names2[@]} " =~ " ${name} " ]]; then
        common_files+=("$name")
    fi
done

# Report missing files
if [[ ${#only_in_path1[@]} -gt 0 ]]; then
    echo -e "${RED}FILES ONLY IN PATH 1:${NC}"
    for file in "${only_in_path1[@]}"; do
        echo -e "  ${RED}- $file${NC}"
    done
    echo ""
fi

if [[ ${#only_in_path2[@]} -gt 0 ]]; then
    echo -e "${RED}FILES ONLY IN PATH 2:${NC}"
    for file in "${only_in_path2[@]}"; do
        echo -e "  ${RED}- $file${NC}"
    done
    echo ""
fi

echo -e "${GREEN}COMMON FILES TO COMPARE: ${#common_files[@]}${NC}"
echo ""

# Compare common files
identical_files=()
different_files=()

for filename in "${common_files[@]}"; do
    file1_path="$PATH1/$filename"
    file2_path="$PATH2/$filename"
    
    if [[ -f "$file1_path" && -f "$file2_path" ]]; then
        # Read and normalize content
        content1=$(normalize_content "$(cat "$file1_path")")
        content2=$(normalize_content "$(cat "$file2_path")")
        
        if [[ "$content1" == "$content2" ]]; then
            identical_files+=("$filename")
            echo -e "${GREEN}✓ IDENTICAL: $filename${NC}"
        else
            different_files+=("$filename")
            echo -e "${RED}✗ DIFFERENT: $filename${NC}"
            
            # Show detailed differences (first 10 lines)
            echo -e "  ${YELLOW}Analyzing differences in $filename...${NC}"
            
            # Create temp files for diff
            temp1=$(mktemp)
            temp2=$(mktemp)
            echo "$content1" > "$temp1"
            echo "$content2" > "$temp2"
            
            # Get diff and show first 10 differences
            diff_output=$(diff -u "$temp1" "$temp2" | head -20)
            if [[ -n "$diff_output" ]]; then
                echo -e "    ${CYAN}First differences:${NC}"
                echo "$diff_output" | head -10 | while read line; do
                    if [[ $line =~ ^- ]]; then
                        echo -e "    ${RED}$line${NC}"
                    elif [[ $line =~ ^+ ]]; then
                        echo -e "    ${GREEN}$line${NC}"
                    else
                        echo "    $line"
                    fi
                done
            fi
            
            # Count total differences
            diff_count=$(diff "$temp1" "$temp2" | wc -l)
            echo -e "  ${YELLOW}Total differences: $diff_count lines${NC}"
            
            # Cleanup temp files
            rm -f "$temp1" "$temp2"
        fi
    else
        different_files+=("$filename")
        echo -e "${RED}✗ ERROR comparing $filename: File not accessible${NC}"
    fi
    
    echo ""
done

# Final summary
echo -e "${CYAN}=== SUMMARY ===${NC}"
echo -e "${WHITE}Total common files: ${#common_files[@]}${NC}"
echo -e "${GREEN}Identical files: ${#identical_files[@]}${NC}"
echo -e "${RED}Different files: ${#different_files[@]}${NC}"

missing_count=$((${#only_in_path1[@]} + ${#only_in_path2[@]}))
if [[ $missing_count -gt 0 ]]; then
    echo -e "${RED}Missing files: $missing_count${NC}"
fi

echo ""

if [[ ${#identical_files[@]} -gt 0 ]]; then
    echo -e "${GREEN}IDENTICAL FILES:${NC}"
    for file in "${identical_files[@]}"; do
        echo -e "  ${GREEN}✓ $file${NC}"
    done
    echo ""
fi

if [[ ${#different_files[@]} -gt 0 ]]; then
    echo -e "${RED}DIFFERENT FILES:${NC}"
    for file in "${different_files[@]}"; do
        echo -e "  ${RED}✗ $file${NC}"
    done
    echo ""
    echo -e "${YELLOW}RECOMMENDATION: Check the mapping logic for these files in ImportInvoicesHandler${NC}"
fi

if [[ ${#different_files[@]} -eq 0 && $missing_count -eq 0 ]]; then
    echo -e "${GREEN}🎉 ALL FILES ARE IDENTICAL (ignoring DateCreated)!${NC}"
    exit 0
else
    exit 1
fi