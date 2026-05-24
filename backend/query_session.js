const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const session = await prisma.session.findUnique({
    where: { id: '51456c61-03b4-4f5a-860c-9c4b55332ac3' },
  });
  console.dir(session, { depth: null });
}

main().finally(() => prisma.$disconnect());
