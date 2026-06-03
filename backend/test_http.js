const http = require('http');
http.get('http://127.0.0.1:39090/api/v1/members/c7749984-5cde-4e96-957f-3628a510441c/avatars', (res) => {
    let data = '';
    res.on('data', chunk => data += chunk);
    res.on('end', () => console.log('Response:', res.statusCode, data));
}).on('error', console.error);
