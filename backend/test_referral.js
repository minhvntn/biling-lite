const crypto = require('crypto');

async function testReferral() {
  console.log('Testing Referral API...');
  try {
    const username1 = 'test' + crypto.randomBytes(4).toString('hex');
    console.log('Creating Member 1:', username1);
    
    // Create member 1
    const res1 = await fetch('http://localhost:39090/api/v1/members', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-API-KEY': 'SmbSecureLANKey2026' },
      body: JSON.stringify({
        username: username1,
        password: 'password123',
        fullName: 'Referrer User',
        memberType: 'REGULAR'
      })
    });
    
    if (!res1.ok) throw new Error(await res1.text());
    const data1 = await res1.json();
    console.log('data1:', data1);
    
    const member1 = data1.member || data1;
    const refCode = member1.referralCode;
    console.log(`Member 1 created successfully. Referral Code: ${refCode}`);
    
    const username2 = 'test' + crypto.randomBytes(4).toString('hex');
    console.log(`Creating Member 2 (${username2}) using referral code: ${refCode}`);
    
    // Create member 2
    const res2 = await fetch('http://localhost:39090/api/v1/members', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-API-KEY': 'SmbSecureLANKey2026' },
      body: JSON.stringify({
        username: username2,
        password: 'password123',
        fullName: 'Referred User',
        memberType: 'REGULAR',
        referredByCode: refCode
      })
    });
    
    if (!res2.ok) throw new Error(await res2.text());
    console.log(`Member 2 created successfully.`);
    
    // Check Member 1 points
    const res3 = await fetch('http://localhost:39090/api/v1/members/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-API-KEY': 'SmbSecureLANKey2026' },
      body: JSON.stringify({
        username: username1,
        password: 'password123'
      })
    });
    
    if (!res3.ok) throw new Error(await res3.text());
    const data3 = await res3.json();
    
    const points = data3.member.availablePoints;
    console.log(`Member 1 available points: ${points}`);
    
    if (points > 0) {
      console.log('SUCCESS: Referral points awarded!');
    } else {
      console.log('FAILED: No points awarded.');
    }
    
  } catch (err) {
    console.error('Error:', err.message);
  }
}

testReferral();
