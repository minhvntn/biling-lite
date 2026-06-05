const fs = require('fs');

const filePath = 'i:/servermanagerbilling/backend/src/members/members.service.ts';
let content = fs.readFileSync(filePath, 'utf8');

// Replace any CRLF with LF to normalize
const normalizedContent = content.replace(/\r\n/g, '\n');

const targetStr = `isActive: member.isActive,\n      createdAt: member.createdAt.toISOString(),`;
const replacementStr = `isActive: member.isActive,\n      comboExpiresAt: member.comboExpiresAt ? member.comboExpiresAt.toISOString() : null,\n      createdAt: member.createdAt.toISOString(),`;

if (normalizedContent.includes(targetStr)) {
  const result = normalizedContent.replace(targetStr, replacementStr);
  // Optional: Convert back to CRLF or let the system handle it
  fs.writeFileSync(filePath, result, 'utf8');
  console.log('Successfully patched members.service.ts');
} else {
  console.log('Target string not found.');
}
