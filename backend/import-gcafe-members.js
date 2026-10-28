/**
 * Script import hội viên từ GCafe Billing sang ServerManagerBilling (PostgreSQL)
 * 
 * Hướng dẫn chạy:
 *   Dry run (kiểm tra trước, không ghi DB):
 *     node import-gcafe-members.js --dry-run
 * 
 *   Thực hiện import chính thức vào DB:
 *     node import-gcafe-members.js --commit
 */

const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const bcrypt = require('bcrypt');
const { PrismaClient } = require('@prisma/client');

const prisma = new PrismaClient();

const GCAFE_DIR = 'C:/Users/minhv/.gemini/antigravity-ide/brain/d7aa102b-9deb-4ad0-be4e-49bf18d8fef3/scratch/cyberindo-dumper/dist';
const BCRYPT_ROUNDS = 10;

function queryGcafe(sql, outFile) {
  try {
    const singleLineSql = sql.replace(/\s+/g, ' ').trim();
    execSync(`console.exe -n Main.gbsdat -c "${singleLineSql}" -f ${outFile}`, {
      cwd: GCAFE_DIR,
      stdio: 'pipe',
    });
  } catch (e) {
    // console.exe returns exit code 1 even on success
  }
  const filePath = path.join(GCAFE_DIR, outFile);
  if (fs.existsSync(filePath)) {
    return fs.readFileSync(filePath, 'utf8');
  }
  return null;
}

function parseCsv(csvContent) {
  const lines = csvContent.split(/\r?\n/).filter(line => line.trim().length > 0);
  if (lines.length < 2) return [];

  // Parse header
  const headerLine = lines[0];
  const headers = headerLine.split(',').map(h => h.replace(/^"|"$/g, '').trim());

  const rows = [];
  for (let i = 1; i < lines.length; i++) {
    const line = lines[i];
    // Regex matches comma-separated values with optional quotes
    const values = [];
    let current = '';
    let inQuotes = false;

    for (let c = 0; c < line.length; c++) {
      const char = line[c];
      if (char === '"') {
        inQuotes = !inQuotes;
      } else if (char === ',' && !inQuotes) {
        values.push(current.replace(/^"|"$/g, '').trim());
        current = '';
      } else {
        current += char;
      }
    }
    values.push(current.replace(/^"|"$/g, '').trim());

    if (values.length >= headers.length) {
      const row = {};
      headers.forEach((h, idx) => {
        row[h] = values[idx] || '';
      });
      rows.push(row);
    }
  }

  return rows;
}

async function main() {
  const isCommit = process.argv.includes('--commit');
  const isDryRun = !isCommit;

  console.log('===============================================================');
  console.log(`BẮT ĐẦU IMPORT HỘI VIÊN TỪ GCAFE (${isCommit ? 'CHÍNH THỨC GHI DB' : 'CHẾ ĐỘ THỬ NGHIỆM - DRY RUN'})`);
  console.log('===============================================================');

  // 1. Trích xuất dữ liệu thô từ GCafe
  console.log('\n[1/5] Đang trích xuất dữ liệu hội viên từ GCafe (Main.gbsdat)...');
  const rawSql = `
    SELECT 
      u.id, 
      u.name as username, 
      u.firstName, 
      u.money, 
      u.freeMoney, 
      u.topupAmount, 
      u.usedAmount, 
      u.usedMinutes, 
      u.mrLoginTime, 
      u.enabled,
      ud.phone, 
      ud.NRIC, 
      ud.bonusPoint
    FROM Users u
    LEFT JOIN UserDetails ud ON u.id = ud.accId
    WHERE u.groupid = 2
    ORDER BY u.id ASC
  `;

  queryGcafe(rawSql, 'export_members.csv');
  const csvData = fs.readFileSync(path.join(GCAFE_DIR, 'export_members.csv'), 'utf8');
  const rawRows = parseCsv(csvData);

  console.log(`=> Đã trích xuất ${rawRows.length} dòng dữ liệu hội viên GCafe.`);

  // 2. Gom nhóm theo Username (Deduplicate & Aggregate)
  console.log('\n[2/5] Đang phân tích và gom nhóm theo tài khoản (Deduplicate)...');
  const memberMap = new Map();

  for (const r of rawRows) {
    const rawUsername = (r.username || '').trim();
    if (!rawUsername) continue;

    const lowerKey = rawUsername.toLowerCase();
    const money = parseFloat(r.money) || 0;
    const freeMoney = parseFloat(r.freeMoney) || 0;
    const topupAmount = parseFloat(r.topupAmount) || 0;
    const usedMinutes = parseInt(r.usedMinutes, 10) || 0;
    const mrLoginTime = parseInt(r.mrLoginTime, 10) || 0;
    const phone = (r.phone || '').trim() || null;
    const nric = (r.NRIC || '').trim() || null;
    const fullName = (r.firstName || '').trim() || rawUsername;

    if (!memberMap.has(lowerKey)) {
      memberMap.set(lowerKey, {
        username: rawUsername, // Giữ format hoa/thường gốc
        fullName,
        phone,
        identityNumber: nric,
        totalMoney: money,
        totalFreeMoney: freeMoney,
        totalTopup: topupAmount,
        totalUsedMinutes: usedMinutes,
        lastLoginTime: mrLoginTime,
        rowCount: 1,
      });
    } else {
      const existing = memberMap.get(lowerKey);
      existing.totalMoney += money;
      existing.totalFreeMoney += freeMoney;
      existing.totalTopup += topupAmount;
      existing.totalUsedMinutes += usedMinutes;
      existing.rowCount += 1;
      if (mrLoginTime > existing.lastLoginTime) {
        existing.lastLoginTime = mrLoginTime;
      }
      if (phone && !existing.phone) existing.phone = phone;
      if (nric && !existing.identityNumber) existing.identityNumber = nric;
      if (fullName && fullName !== rawUsername && existing.fullName === existing.username) {
        existing.fullName = fullName;
      }
    }
  }

  console.log(`=> Tổng số tài khoản hội viên duy nhất: ${memberMap.size} tài khoản.`);

  // 3. Đọc dữ liệu hội viên hiện có trong Database ServerManagerBilling
  console.log('\n[3/5] Đang kiểm tra danh sách hội viên hiện tại trong database PostgreSQL...');
  const currentMembers = await prisma.member.findMany({
    select: {
      id: true,
      username: true,
      fullName: true,
      phone: true,
      balance: true,
      totalTopup: true,
      totalUsageSeconds: true,
    },
  });

  const existingDbMap = new Map();
  for (const m of currentMembers) {
    existingDbMap.set(m.username.toLowerCase(), m);
  }
  console.log(`=> Database hiện có: ${currentMembers.length} hội viên.`);

  // 4. Chuẩn bị danh sách tạo mới và cập nhật cộng dồn
  console.log('\n[4/5] Chuẩn bị dữ liệu import và hash mật khẩu ngẫu nhiên...');
  let toCreate = [];
  let toMerge = [];
  let totalBalanceToImport = 0;
  let membersWithPositiveBalance = 0;

  for (const item of memberMap.values()) {
    // Làm tròn số dư: nếu số dư < 1 VNĐ (do epsilon float của GCafe) thì = 0
    const cleanBalance = item.totalMoney >= 1 ? Math.round(item.totalMoney) : 0;
    const cleanTopup = Math.round(item.totalTopup);
    const cleanUsageSeconds = item.totalUsedMinutes * 60;

    if (cleanBalance > 0) {
      membersWithPositiveBalance++;
      totalBalanceToImport += cleanBalance;
    }

    const lower = item.username.toLowerCase();
    if (existingDbMap.has(lower)) {
      toMerge.push({
        existing: existingDbMap.get(lower),
        gcafeData: {
          ...item,
          balance: cleanBalance,
          topup: cleanTopup,
          usageSeconds: cleanUsageSeconds,
        },
      });
    } else {
      toCreate.push({
        username: item.username,
        fullName: item.fullName,
        phone: item.phone,
        identityNumber: item.identityNumber,
        balance: cleanBalance,
        totalTopup: cleanTopup,
        totalUsageSeconds: cleanUsageSeconds,
        lastLoginTime: item.lastLoginTime,
      });
    }
  }

  console.log('---------------------------------------------------------------');
  console.log('THỐNG KÊ CHI TIẾT DỮ LIỆU IMPORT:');
  console.log(` - Tổng số tài khoản GCafe sau khi gộp trùng: ${memberMap.size}`);
  console.log(` - Số tài khoản sẽ TẠO MỚI: ${toCreate.length}`);
  console.log(` - Số tài khoản TRÙNG (sẽ CỘNG DỒN số dư): ${toMerge.length}`);
  console.log(` - Số tài khoản có số dư tiền > 0: ${membersWithPositiveBalance}`);
  console.log(` - Tổng số tiền số dư sẽ import vào hệ thống: ${totalBalanceToImport.toLocaleString('vi-VN')} VNĐ`);
  console.log('---------------------------------------------------------------');

  if (toMerge.length > 0) {
    console.log('\nDanh sách tài khoản trùng sẽ được cộng dồn số dư:');
    for (const m of toMerge) {
      console.log(` * ${m.existing.username}: Số dư hiện tại = ${Number(m.existing.balance).toLocaleString('vi-VN')} đ + GCafe = ${m.gcafeData.balance.toLocaleString('vi-VN')} đ => Mới = ${(Number(m.existing.balance) + m.gcafeData.balance).toLocaleString('vi-VN')} đ`);
    }
  }

  if (isDryRun) {
    console.log('\n[CHẾ ĐỘ DRY-RUN] ĐÃ HOÀN TẤT KIỂM TRA. Không có dữ liệu nào bị thay đổi trong database.');
    console.log('Để thực hiện import chính thức, hãy chạy:');
    console.log('   node import-gcafe-members.js --commit\n');
    await prisma.$disconnect();
    return;
  }

  // 5. Thực hiện Import vào PostgreSQL
  console.log('\n[5/5] Đang ghi dữ liệu vào database PostgreSQL...');

  // Xử lý cộng dồn cho tài khoản trùng
  for (const m of toMerge) {
    const newBalance = Number(m.existing.balance) + m.gcafeData.balance;
    const newTopup = Number(m.existing.totalTopup) + m.gcafeData.topup;
    const newUsage = Number(m.existing.totalUsageSeconds) + m.gcafeData.usageSeconds;

    await prisma.member.update({
      where: { id: m.existing.id },
      data: {
        balance: newBalance,
        totalTopup: newTopup,
        totalUsageSeconds: newUsage,
        phone: m.existing.phone || m.gcafeData.phone,
        identityNumber: m.gcafeData.identityNumber || undefined,
      },
    });

    if (m.gcafeData.balance > 0) {
      await prisma.memberTransaction.create({
        data: {
          memberId: m.existing.id,
          type: 'ADJUSTMENT',
          amountDelta: m.gcafeData.balance,
          note: `Cộng dồn số dư chuyển đổi từ GCafe (${m.gcafeData.balance.toLocaleString('vi-VN')} đ)`,
          createdBy: 'GCafe Migration',
        },
      });
    }
  }
  console.log(`=> Đã cập nhật cộng dồn cho ${toMerge.length} tài khoản trùng.`);

  // Xử lý tạo mới tài khoản
  console.log(`Đang tạo ${toCreate.length} tài khoản mới (vui lòng chờ trong giây lát)...`);
  const batchSize = 100;
  let createdCount = 0;

  // Theo dõi referralCode để đảm bảo không trùng lặp (unique constraint)
  const usedReferralCodes = new Set();
  const existingCodes = await prisma.member.findMany({ select: { referralCode: true } });
  for (const c of existingCodes) {
    if (c.referralCode) usedReferralCodes.add(c.referralCode);
  }

  function generateUniqueReferralCode() {
    while (true) {
      const code = crypto.randomBytes(4).toString('hex').toUpperCase();
      if (!usedReferralCodes.has(code)) {
        usedReferralCodes.add(code);
        return code;
      }
    }
  }

  for (let i = 0; i < toCreate.length; i += batchSize) {
    const batch = toCreate.slice(i, i + batchSize);

    for (const item of batch) {
      // Tạo mật khẩu ngẫu nhiên bảo mật 16 ký tự và hash bằng bcrypt
      const randomPassword = crypto.randomBytes(8).toString('hex'); // 16 hex chars
      const passwordHash = await bcrypt.hash(randomPassword, BCRYPT_ROUNDS);

      // Tạo referral code ngẫu nhiên duy nhất
      const referralCode = generateUniqueReferralCode();

      const createdAt = item.lastLoginTime > 0 
        ? new Date(item.lastLoginTime * 1000) 
        : new Date();

      const createdMember = await prisma.member.create({
        data: {
          username: item.username,
          fullName: item.fullName,
          phone: item.phone,
          identityNumber: item.identityNumber,
          balance: item.balance,
          totalTopup: item.totalTopup,
          totalUsageSeconds: item.totalUsageSeconds,
          passwordHash,
          plainPassword: null, // Không lưu plain password để khách bảo mật
          isActive: true,
          memberType: 'REGULAR',
          referralCode,
          createdAt,
        },
      });

      // Nếu có số dư ban đầu, ghi log transaction
      if (item.balance > 0) {
        await prisma.memberTransaction.create({
          data: {
            memberId: createdMember.id,
            type: 'ADJUSTMENT',
            amountDelta: item.balance,
            note: `Số dư ban đầu chuyển đổi từ GCafe (${item.balance.toLocaleString('vi-VN')} đ)`,
            createdBy: 'GCafe Migration',
          },
        });
      }

      createdCount++;
    }

    process.stdout.write(`Đã import: ${createdCount}/${toCreate.length} tài khoản...\r`);
  }

  console.log(`\n=> THÀNH CÔNG! Đã tạo mới ${createdCount} tài khoản hội viên.`);
  const finalCount = await prisma.member.count();
  console.log(`=> TỔNG SỐ HỘI VIÊN HIỆN CÓ TRONG DATABASE: ${finalCount} hội viên.`);
  console.log('===============================================================');

  await prisma.$disconnect();
}

main().catch(async (e) => {
  console.error('LỖI IMPORT:', e);
  await prisma.$disconnect();
  process.exit(1);
});
