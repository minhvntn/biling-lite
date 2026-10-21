const http = require('http');

const API_BASE = 'http://localhost:39090/api/v1';
const API_KEY = 'SmbSecureLANKey2026';

function request(method, path, body = null) {
  return new Promise((resolve, reject) => {
    const url = new URL(API_BASE + path);
    const options = {
      hostname: url.hostname,
      port: url.port,
      path: url.pathname + url.search,
      method: method,
      headers: {
        'x-api-key': API_KEY,
        'Content-Type': 'application/json',
      },
    };

    const req = http.request(options, (res) => {
      let data = '';
      res.on('data', (chunk) => (data += chunk));
      res.on('end', () => {
        try {
          const parsed = JSON.parse(data);
          resolve({ status: res.statusCode, body: parsed });
        } catch (e) {
          resolve({ status: res.statusCode, raw: data });
        }
      });
    });

    req.on('error', reject);
    if (body) {
      req.write(JSON.stringify(body));
    }
    req.end();
  });
}

async function runTests() {
  console.log('=== BẮT ĐẦU KIỂM TRA TÍNH NĂNG NẠP GIỜ CHƠI / NẠP TIỀN HỘI VIÊN BÌNH THƯỜNG ===\n');

  // 1. Lấy thông tin giá & khuyến mãi hiện tại
  const pricingRes = await request('GET', '/pricing');
  const clientSettingsRes = await request('GET', '/pricing/client-settings');
  
  const defaultMemberRate = pricingRes.body.defaultMemberRatePerHour || 6000;
  console.log(`[Cấu hình giá]`);
  console.log(`- Đơn giá giờ hội viên mặc định: ${defaultMemberRate.toLocaleString('vi-VN')} VND/giờ (~${(defaultMemberRate/60).toFixed(0)} VND/phút)`);
  console.log(`- Khuyến mãi nạp tiền kích hoạt: ${clientSettingsRes.body.topupPromoEnabled}`);
  console.log(`- Bậc khuyến mãi: ${clientSettingsRes.body.topupPromoTiers}`);

  const promoTiers = JSON.parse(clientSettingsRes.body.topupPromoTiers || '[]');

  // 2. Tìm hoặc tạo hội viên kiểm tra
  console.log('\n[Khởi tạo hội viên kiểm tra]');
  let memberRes = await request('GET', '/members?search=test_regular_topup');
  let member;
  if (memberRes.body.items && memberRes.body.items.length > 0) {
    member = memberRes.body.items[0];
    console.log(`- Đã tìm thấy hội viên có sẵn: ${member.username} (ID: ${member.id})`);
  } else {
    const createRes = await request('POST', '/members', {
      username: 'test_regular_topup',
      fullName: 'Test Regular Member Topup',
      password: '123',
      memberType: 'REGULAR',
    });
    if (createRes.status >= 400) {
      console.error('Lỗi tạo hội viên:', createRes.body);
      return;
    }
    member = createRes.body;
    console.log(`- Đã tạo hội viên mới: ${member.username} (ID: ${member.id})`);
  }

  // Lấy trạng thái hiện tại
  memberRes = await request('GET', `/members?search=${member.username}`);
  member = memberRes.body.items[0];
  const initialBalance = Number(member.balance);
  const initialTotalTopup = Number(member.totalTopup);
  console.log(`- Số dư ban đầu: ${initialBalance.toLocaleString('vi-VN')} VND`);
  console.log(`- Tổng nạp ban đầu: ${initialTotalTopup.toLocaleString('vi-VN')} VND`);

  // TEST 1: Nạp số tiền không nằm trong khung KM (< 10,000 VND)
  console.log('\n--------------------------------------------------------------');
  console.log('TEST 1: Nạp 5,000 VND (dưới mức khuyến mãi 10,000 VND)');
  const topup1 = await request('POST', `/members/${member.id}/topups`, {
    amount: 5000,
    createdBy: 'test.runner',
    note: 'Test nạp không KM',
  });
  const m1 = topup1.body.member;
  const expectedB1 = initialBalance + 5000;
  const expectedT1 = initialTotalTopup + 5000;
  const pass1 = m1.balance === expectedB1 && m1.totalTopup === expectedT1;
  console.log(`- Số dư sau nạp: ${m1.balance.toLocaleString('vi-VN')} VND (Kỳ vọng: ${expectedB1.toLocaleString('vi-VN')} VND)`);
  console.log(`- Tổng nạp sau nạp: ${m1.totalTopup.toLocaleString('vi-VN')} VND (Kỳ vọng: ${expectedT1.toLocaleString('vi-VN')} VND)`);
  console.log(`- Ghi chú giao dịch: "${topup1.body.transaction.note}"`);
  console.log(`=> KẾT QUẢ TEST 1: ${pass1 ? 'CHÍNH XÁC (PASS)' : 'SAI LỆCH (FAIL)'}`);

  // TEST 2: Nạp 10,000 VND (khung KM 1: thưởng 5% = +500 VND)
  console.log('\n--------------------------------------------------------------');
  console.log('TEST 2: Nạp 10,000 VND (Bậc 1: KM 5% = 500 VND)');
  const topup2 = await request('POST', `/members/${member.id}/topups`, {
    amount: 10000,
    createdBy: 'test.runner',
    note: 'Test nạp bậc 1',
  });
  const m2 = topup2.body.member;
  const expectedB2 = m1.balance + 10500; // 10,000 + 500 bonus
  const expectedT2 = m1.totalTopup + 10000; // totalTopup chỉ cộng gốc
  const pass2 = m2.balance === expectedB2 && m2.totalTopup === expectedT2;
  console.log(`- Số dư sau nạp: ${m2.balance.toLocaleString('vi-VN')} VND (Kỳ vọng: ${expectedB2.toLocaleString('vi-VN')} VND)`);
  console.log(`- Tổng nạp sau nạp: ${m2.totalTopup.toLocaleString('vi-VN')} VND (Kỳ vọng: ${expectedT2.toLocaleString('vi-VN')} VND)`);
  console.log(`- Ghi chú giao dịch: "${topup2.body.transaction.note}"`);
  console.log(`=> KẾT QUẢ TEST 2: ${pass2 ? 'CHÍNH XÁC (PASS)' : 'SAI LỆCH (FAIL)'}`);

  // TEST 3: Nạp 20,000 VND (khung KM 2: thưởng 10% = +2,000 VND)
  console.log('\n--------------------------------------------------------------');
  console.log('TEST 3: Nạp 20,000 VND (Bậc 2: KM 10% = 2,000 VND)');
  const topup3 = await request('POST', `/members/${member.id}/topups`, {
    amount: 20000,
    createdBy: 'test.runner',
    note: 'Test nạp bậc 2',
  });
  const m3 = topup3.body.member;
  const expectedB3 = m2.balance + 22000; // 20,000 + 2,000 bonus
  const expectedT3 = m2.totalTopup + 20000;
  const pass3 = m3.balance === expectedB3 && m3.totalTopup === expectedT3;
  console.log(`- Số dư sau nạp: ${m3.balance.toLocaleString('vi-VN')} VND (Kỳ vọng: ${expectedB3.toLocaleString('vi-VN')} VND)`);
  console.log(`- Tổng nạp sau nạp: ${m3.totalTopup.toLocaleString('vi-VN')} VND (Kỳ vọng: ${expectedT3.toLocaleString('vi-VN')} VND)`);
  console.log(`- Ghi chú giao dịch: "${topup3.body.transaction.note}"`);
  console.log(`=> KẾT QUẢ TEST 3: ${pass3 ? 'CHÍNH XÁC (PASS)' : 'SAI LỆCH (FAIL)'}`);

  // TEST 4: Quy đổi Giờ chơi <-> Tiền nạp theo công thức Server Admin App & Client Agent
  console.log('\n--------------------------------------------------------------');
  console.log('TEST 4: Quy đổi Giờ chơi <-> Số tiền theo giá nhóm máy');
  const ratePerHour = defaultMemberRate; // 6,000 VND/h
  const pricePerMinute = ratePerHour / 60; // 100 VND/phút
  console.log(`- Đơn giá áp dụng: ${ratePerHour.toLocaleString('vi-VN')} VND/giờ = ${pricePerMinute.toLocaleString('vi-VN')} VND/phút`);

  const testCasesMinutes = [30, 60, 90, 120, 150];
  testCasesMinutes.forEach(minutes => {
    const rawMoney = (minutes / 60) * ratePerHour;
    const convertedTopupAmount = Math.max(1, Math.ceil(rawMoney));
    // Check promo bonus on this amount
    let bonus = 0;
    const matchedTier = promoTiers.filter(t => convertedTopupAmount >= t.MinAmount).sort((a,b) => b.MinAmount - a.MinAmount)[0];
    if (matchedTier) {
      bonus = Math.round(convertedTopupAmount * (matchedTier.BonusRate / 100));
    }
    const finalBalanceReceived = convertedTopupAmount + bonus;
    const finalPlaySeconds = (finalBalanceReceived / pricePerMinute) * 60;
    const finalPlayMinutes = finalPlaySeconds / 60;
    console.log(`  + Nạp ${minutes} phút (${minutes/60}h) -> Cần nạp: ${convertedTopupAmount.toLocaleString('vi-VN')} VND | Khuyến mãi: +${bonus.toLocaleString('vi-VN')} VND -> Nhận ${finalBalanceReceived.toLocaleString('vi-VN')} VND = ${finalPlayMinutes} phút chơi (~${(finalPlayMinutes/60).toFixed(2)}h)`);
  });

  // TEST 5: Kiểm tra Lịch sử Giao dịch
  console.log('\n--------------------------------------------------------------');
  console.log('TEST 5: Kiểm tra Lịch sử Giao dịch (Transaction Log)');
  const txRes = await request('GET', `/members/${member.id}/transactions`);
  const txList = txRes.body.items || txRes.body;
  console.log(`- Tổng số giao dịch được ghi nhận: ${txList.length}`);
  const topupTxs = txList.filter(t => t.type === 'TOPUP');
  console.log(`- Số giao dịch nạp tiền: ${topupTxs.length}`);
  topupTxs.slice(0, 3).forEach((tx, idx) => {
    console.log(`  ${idx + 1}. [${new Date(tx.createdAt).toLocaleTimeString('vi-VN')}] Số tiền: +${Number(tx.amountDelta).toLocaleString('vi-VN')} VND | Ghi chú: ${tx.note} | Người tạo: ${tx.createdBy}`);
  });

  // TEST 6: Kiểm tra Tính năng Yêu cầu nạp tiền từ Máy trạm (Client) & Chống Spam / Cooldown
  console.log('\n--------------------------------------------------------------');
  console.log('TEST 6: Kiểm tra Yêu cầu nạp tiền từ máy trạm (Client Self-Topup) & Chống Spam');
  const freshMemberName = `test_client_${Date.now().toString().slice(-4)}`;
  const freshMemberRes = await request('POST', '/members', {
    username: freshMemberName,
    fullName: 'Test Client Member',
    password: '123',
    memberType: 'REGULAR',
  });
  const freshMember = freshMemberRes.body;
  console.log(`- Tạo hội viên mới: ${freshMember.username} (Số dư ban đầu: 0 VND)`);

  const reqTopupRes = await request('POST', `/members/${freshMember.id}/topup-request`, {
    amount: 15000,
    note: 'Yêu cầu nạp tiền từ máy 01',
    agentId: 'pc-01',
  });
  console.log(`- Gửi yêu cầu nạp 15,000 VND: Mã phản hồi HTTP ${reqTopupRes.status}`);
  if (reqTopupRes.status === 201 || reqTopupRes.status === 200) {
    const requestId = reqTopupRes.body.request?.requestId || reqTopupRes.body.request?.id;
    console.log(`  + Đã tạo yêu cầu nạp tiền (Request ID: ${requestId}, Trạng thái: ${reqTopupRes.body.status})`);

    // Thử gửi tiếp yêu cầu thứ 2 ngay lập tức -> Phải bị chặn do đang PENDING
    const spamRes1 = await request('POST', `/members/${freshMember.id}/topup-request`, {
      amount: 20000,
      agentId: 'pc-01',
    });
    console.log(`- Thử gửi tiếp khi yêu cầu trước chưa duyệt: Mã HTTP ${spamRes1.status} - Lỗi: "${spamRes1.body.message}"`);
    const passSpamPending = spamRes1.status === 400 && spamRes1.body.message.includes('chưa được xử lý');
    console.log(`  => Chống spam khi PENDING: ${passSpamPending ? 'CHÍNH XÁC (PASS)' : 'FAIL'}`);

    // Thu ngân duyệt yêu cầu
    const approveRes = await request('POST', `/members/topup-requests/${requestId}/approve`, {
      approvedBy: 'admin.desktop',
    });
    console.log(`- Thu ngân duyệt yêu cầu: Mã HTTP ${approveRes.status}`);
    const balanceAfterApproval = approveRes.body.member.balance;
    console.log(`  + Số dư sau duyệt: ${balanceAfterApproval.toLocaleString('vi-VN')} VND (Đã cộng KM 5% của 15k = +750 VND -> Nhận 15,750 VND)`);

    // Thử gửi tiếp ngay sau khi đã duyệt -> Phải bị chặn bởi Cooldown 5 phút
    const spamRes2 = await request('POST', `/members/${freshMember.id}/topup-request`, {
      amount: 10000,
      agentId: 'pc-01',
    });
    console.log(`- Thử gửi tiếp ngay sau khi vừa duyệt: Mã HTTP ${spamRes2.status} - Lỗi: "${spamRes2.body.message}"`);
    const passCooldown = spamRes2.status === 400 && spamRes2.body.message.includes('phút');
    console.log(`  => Cooldown 5 phút chặn spam: ${passCooldown ? 'CHÍNH XÁC (PASS)' : 'FAIL'}`);
  } else {
    console.log(`- Không thể tạo yêu cầu nạp tiền:`, reqTopupRes.body);
  }

  console.log('\n=== HOÀN TẤT KIỂM TRA ===');
}

runTests().catch(err => {
  console.error('Lỗi khi chạy test:', err);
});
