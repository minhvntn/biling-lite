const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function run() {
    console.log('--- Rebuilding VIP Rank System (Max 100M VND) ---');

    // 1. Clear existing ranks
    await prisma.loyaltyRankConfig.deleteMany({});
    console.log('Cleared existing ranks.');

    const categories = [
        'Sắt', 'Đồng', 'Bạc', 'Vàng', 'Bạch Kim', 
        'Tinh Anh', 'Kim Cương', 'Cao Thủ', 'Đại Cao Thủ', 'Thách Đấu'
    ];
    const tiersPerCategory = 10;
    const totalRanks = categories.length * tiersPerCategory; // 100

    const maxTopup = 100000000; // 100,000,000 VND
    const startMinutes = 150;    // Iron 1
    const endMinutes = 15;      // Challenger 10
    const startBonus = 0;       // Iron 1
    const endBonus = 100;       // Challenger 10 (100% bonus as requested)

    for (let i = 0; i < totalRanks; i++) {
        const catIndex = Math.floor(i / tiersPerCategory);
        const tier = (i % tiersPerCategory) + 1;
        const rankName = `${categories[catIndex]} ${tier}`;

        // Linear interpolation factor (0 to 1)
        const factor = i / (totalRanks - 1);

        // Calculate values
        const minTopup = Math.round(maxTopup * factor);
        const minutesPerPoint = Math.round(startMinutes - (startMinutes - endMinutes) * factor);
        const bonusPercent = Math.round(startBonus + (endBonus - startBonus) * factor);

        await prisma.loyaltyRankConfig.create({
            data: {
                rankName,
                minTopup,
                bonusPercent,
                minutesPerPoint
            }
        });

        if ((i + 1) % 10 === 0) {
            console.log(`Created up to ${rankName}: Threshold ${minTopup.toLocaleString()} VND, ${minutesPerPoint}m/pt, ${bonusPercent}% bonus`);
        }
    }

    console.log('--- Successfully rebuilt 100 VIP ranks! ---');
}

run()
    .catch(console.error)
    .finally(() => prisma.$disconnect());
